using GuruBook.Modules.Sms.Client;
using GuruBook.Modules.Sms.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Phone;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Sms.Services;

/// <summary>
/// Decides which drafts can go (a number, not asked to stop, not sent before, inside the month's allowance of parts) and queues them. The
/// background sender does the actual sending, so the teacher's request returns at once. Preview and send share one decision, so the preview is
/// what send does. The words come from <see cref="MessageTexts"/>, or from the teacher's own wording when they wrote some.
/// </summary>
internal sealed class SmsSender(
    SmsDbContext db, IStudentContacts contacts, IPlanGate plans, ITeacherDirectory teachers, ISmsClient client,
    IMessageSettings messages, ITenantContext tenant, SmsSignal signal, SmsDispatchRunner runner, TimeProvider clock) : ISmsSender
{
    public async Task<string> ChannelAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return MessageChannels.IsKnown(row?.Channel) ? row!.Channel : MessageChannels.WhatsAppThenSms;
    }

    public async Task<SmsPreview> PreviewAsync(IReadOnlyList<SmsDraft> drafts, CancellationToken ct = default)
    {
        var decision = await DecideAsync(drafts, null, ct);
        return new SmsPreview(
            client.IsConfigured, drafts.Count, decision.Ready.Count, decision.Ready.Sum(r => r.Parts),
            decision.NoNumber.Count, decision.OptedOut, decision.AlreadySent, decision.Over,
            decision.Limit, decision.Used, [.. decision.NoNumber]);
    }

    public Task<SmsSendResult> SendAsync(IReadOnlyList<SmsDraft> drafts, CancellationToken ct = default) => QueueAsync(drafts, null, ct);

    public Task<SmsSendResult> SendForAsync(Guid teacherId, IReadOnlyList<SmsDraft> drafts, CancellationToken ct = default) =>
        QueueAsync(drafts, teacherId, ct);

    public async Task<bool> TakeOverAsync(Guid teacherId, SmsDraft draft, string toPhone, string studentName, CancellationToken ct = default)
    {
        if (!client.IsConfigured || PhoneNumber.Normalize(toPhone) is not { } phone || !MessageTexts.Has(EventOf(draft)))
        {
            return false;
        }

        var plan = await plans.ForTeacherAsync(teacherId, ct);
        var limit = LimitOf(plan);
        if (limit == 0)
        {
            return false;
        }

        // The background sender acts for nobody, so every read lifts the teacher filter and names the teacher outright.
        var channel = await db.Settings.IgnoreQueryFilters().AsNoTracking().Where(s => s.TenantId == teacherId).Select(s => s.Channel).FirstOrDefaultAsync(ct);
        if (MessageChannels.IsKnown(channel) && channel != MessageChannels.WhatsAppThenSms)
        {
            // Nothing to take over: a teacher who chose SMS only never sent it on WhatsApp.
            return false;
        }

        if (await db.OptOuts.AnyAsync(o => o.Phone == phone, ct))
        {
            return false;
        }

        var month = Month();
        var used = await UsedAsync(month, teacherId, ct);
        var language = MessageTexts.LanguageOf(await teachers.LanguageAsync(teacherId, ct));
        var text = await ComposeAsync(teacherId, draft, language, !await KnownAsync(phone, ct), ct);
        var parts = MessageTexts.Parts(text);
        if (limit is { } cap && used + parts > cap)
        {
            return false;
        }

        db.Messages.Add(new SmsMessage
        {
            TenantId = teacherId,
            StudentId = draft.StudentId,
            StudentName = MessageTexts.Clean(studentName),
            Kind = draft.Kind,
            ToPhone = phone,
            Text = text,
            Language = language,
            Parts = parts,
            DedupKey = draft.DedupKey,
            Month = month,
            Fallback = true,
            Status = SmsStatus.Queued,
        });
        await db.SaveChangesAsync(ct);
        Wake();
        return true;
    }

    private async Task<SmsSendResult> QueueAsync(IReadOnlyList<SmsDraft> drafts, Guid? teacherId, CancellationToken ct)
    {
        if (!client.IsConfigured)
        {
            return new SmsSendResult(false, 0, 0, 0, 0, 0);
        }

        var decision = await DecideAsync(drafts, teacherId, ct);
        foreach (var item in decision.Ready)
        {
            db.Messages.Add(Row(item, Month(), teacherId));
        }

        if (decision.Ready.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            Wake();
        }

        return new SmsSendResult(true, decision.Ready.Count, decision.NoNumber.Count, decision.OptedOut, decision.AlreadySent, decision.Over);
    }

    private void Wake()
    {
        // Wakes the background loop, and also starts a run straight away in case that loop is not running.
        signal.Wake();
        runner.Kick();
    }

    /// <summary>The event a draft belongs to: the one it names, or the one its kind belongs to when a teacher sent it by hand.</summary>
    private static string EventOf(SmsDraft draft) => MessageEvents.IsKnown(draft.Event) ? draft.Event! : MessageEvents.ForKind(draft.Kind);

    private async Task<string> ComposeAsync(Guid teacherId, SmsDraft draft, string language, bool withStopNote, CancellationToken ct)
    {
        var name = EventOf(draft);
        var custom = await messages.TextForAsync(teacherId, name, language, ct);
        return MessageTexts.Compose(name, language, draft.Params, withStopNote, custom);
    }

    private SmsMessage Row(Ready item, string month, Guid? teacherId) => new()
    {
        // A background send names the teacher; a teacher's own send is stamped with theirs.
        TenantId = teacherId ?? Guid.Empty,
        StudentId = item.Draft.StudentId,
        StudentName = MessageTexts.Clean(item.Name),
        Kind = item.Draft.Kind,
        ToPhone = item.Phone,
        Text = item.Text,
        Language = item.Language,
        Parts = item.Parts,
        DedupKey = item.Draft.DedupKey,
        Month = month,
        Status = SmsStatus.Queued,
    };

    private string Month() => clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM");

    private static int? LimitOf(TeacherPlan plan) => plan.Has(PlanFeatures.Sms) ? plan.LimitOf(PlanLimits.SmsPerMonth) : 0;

    /// <summary>Parts used this month. Failed messages are not paid for, so they do not count.</summary>
    private Task<int> UsedAsync(string month, Guid? teacherId, CancellationToken ct) =>
        Rows(teacherId).Where(m => m.Month == month && m.Status != SmsStatus.Failed).SumAsync(m => m.Parts, ct);

    /// <summary>Everything this teacher has sent: named outright for a background run, which acts for nobody.</summary>
    private IQueryable<SmsMessage> Rows(Guid? teacherId) =>
        teacherId is { } id ? db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == id) : db.Messages;

    /// <summary>True once this number has had a text from GuruBook, whoever sent it: then it needs no "you can stop these" line.</summary>
    private Task<bool> KnownAsync(string phone, CancellationToken ct) =>
        db.Messages.IgnoreQueryFilters().AnyAsync(m => m.ToPhone == phone && m.Status != SmsStatus.Failed, ct);

    private sealed record Ready(SmsDraft Draft, string Name, string Phone, string Text, string Language, int Parts);

    private sealed record Decision(List<Ready> Ready, List<Guid> NoNumber, int OptedOut, int AlreadySent, int Over, int? Limit, int Used);

    private async Task<Decision> DecideAsync(IReadOnlyList<SmsDraft> drafts, Guid? teacherId, CancellationToken ct)
    {
        foreach (var draft in drafts)
        {
            var name = EventOf(draft);
            if (!MessageTexts.Has(name) || draft.Params.Count != MessageEvents.Of(name).Fields.Count)
            {
                throw new ArgumentException($"A '{name}' message needs the values its text takes.", nameof(drafts));
            }
        }

        var me = teacherId ?? tenant.TenantId;
        var plan = teacherId is { } owner ? await plans.ForTeacherAsync(owner, ct) : await plans.CurrentAsync(ct);
        var limit = LimitOf(plan);
        var month = Month();
        var used = await UsedAsync(month, teacherId, ct);

        var ids = drafts.Select(d => d.StudentId);
        var found = teacherId is { } forTeacher
            ? await contacts.FindForTeacherAsync(forTeacher, ids, ct)
            : await contacts.FindAsync(ids, ct);
        var stopped = (await db.OptOuts.Select(o => o.Phone).ToListAsync(ct)).ToHashSet();
        var keys = drafts.Select(d => d.DedupKey).Distinct().ToList();
        var sent = (await Rows(teacherId).Where(m => keys.Contains(m.DedupKey) && m.Status != SmsStatus.Failed)
                .Select(m => new { m.DedupKey, m.CreatedAt }).ToListAsync(ct))
            .GroupBy(m => m.DedupKey).ToDictionary(g => g.Key, g => g.Max(m => m.CreatedAt));
        var language = MessageTexts.LanguageOf(me is { } who ? await teachers.LanguageAsync(who, ct) : "en");
        var known = (await db.Messages.IgnoreQueryFilters().Where(m => m.Status != SmsStatus.Failed).Select(m => m.ToPhone).Distinct().ToListAsync(ct))
            .ToHashSet();

        var now = clock.GetUtcNow();
        var ready = new List<Ready>();
        var noNumber = new List<Guid>();
        var seen = new HashSet<string>();
        int optedOut = 0, already = 0;
        foreach (var draft in drafts)
        {
            if (!found.TryGetValue(draft.StudentId, out var contact) || PhoneNumber.Normalize(contact.MessageNumber) is not { } phone)
            {
                noNumber.Add(draft.StudentId);
            }
            else if (stopped.Contains(phone))
            {
                optedOut++;
            }
            else if (!seen.Add(draft.DedupKey) || (sent.TryGetValue(draft.DedupKey, out var at) && (draft.Window is null || now - at < draft.Window)))
            {
                already++;
            }
            else
            {
                var text = me is { } author
                    ? await ComposeAsync(author, draft, language, known.Add(phone), ct)
                    : MessageTexts.Compose(EventOf(draft), language, draft.Params, known.Add(phone));
                ready.Add(new Ready(draft, contact.FullName, phone, text, language, MessageTexts.Parts(text)));
            }
        }

        // The allowance counts parts, so a long Sinhala message uses more of it than a short English one.
        var room = limit is { } cap ? Math.Max(0, cap - used) : int.MaxValue;
        var fits = new List<Ready>();
        var spent = 0;
        foreach (var item in ready)
        {
            if (spent + item.Parts > room)
            {
                break;
            }

            fits.Add(item);
            spent += item.Parts;
        }

        return new Decision(fits, noNumber, optedOut, already, ready.Count - fits.Count, limit, used);
    }
}
