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
/// what send does.
/// </summary>
internal sealed class SmsSender(
    SmsDbContext db, IStudentContacts contacts, IPlanGate plans, ITeacherDirectory teachers, ISmsClient client,
    ITenantContext tenant, SmsSignal signal, SmsDispatchRunner runner, TimeProvider clock) : ISmsSender
{
    public async Task<string> ChannelAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return MessageChannels.IsKnown(row?.Channel) ? row!.Channel : MessageChannels.WhatsAppThenSms;
    }

    public async Task<SmsPreview> PreviewAsync(IReadOnlyList<SmsDraft> drafts, CancellationToken ct = default)
    {
        var decision = await DecideAsync(drafts, ct);
        return new SmsPreview(
            client.IsConfigured, drafts.Count, decision.Ready.Count, decision.Ready.Sum(r => r.Parts),
            decision.NoNumber.Count, decision.OptedOut, decision.AlreadySent, decision.Over,
            decision.Limit, decision.Used, [.. decision.NoNumber]);
    }

    public async Task<SmsSendResult> SendAsync(IReadOnlyList<SmsDraft> drafts, CancellationToken ct = default)
    {
        if (!client.IsConfigured)
        {
            return new SmsSendResult(false, 0, 0, 0, 0, 0);
        }

        var decision = await DecideAsync(drafts, ct);
        foreach (var item in decision.Ready)
        {
            db.Messages.Add(Row(item, Month(), false));
        }

        if (decision.Ready.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            Wake();
        }

        return new SmsSendResult(true, decision.Ready.Count, decision.NoNumber.Count, decision.OptedOut, decision.AlreadySent, decision.Over);
    }

    public async Task<bool> TakeOverAsync(Guid teacherId, SmsDraft draft, string toPhone, string studentName, CancellationToken ct = default)
    {
        if (!client.IsConfigured || PhoneNumber.Normalize(toPhone) is not { } phone || !SmsTexts.IsKnownKind(draft.Kind))
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
        var language = SmsTexts.LanguageOf(await teachers.LanguageAsync(teacherId, ct));
        var text = SmsTexts.Compose(draft.Kind, language, draft.Params, !await KnownAsync(phone, ct));
        var parts = SmsTexts.Parts(text);
        if (limit is { } cap && used + parts > cap)
        {
            return false;
        }

        db.Messages.Add(new SmsMessage
        {
            TenantId = teacherId,
            StudentId = draft.StudentId,
            StudentName = SmsTexts.Clean(studentName),
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

    private void Wake()
    {
        // Wakes the background loop, and also starts a run straight away in case that loop is not running.
        signal.Wake();
        runner.Kick();
    }

    private SmsMessage Row(Ready item, string month, bool fallback) => new()
    {
        StudentId = item.Draft.StudentId,
        StudentName = SmsTexts.Clean(item.Name),
        Kind = item.Draft.Kind,
        ToPhone = item.Phone,
        Text = item.Text,
        Language = item.Language,
        Parts = item.Parts,
        DedupKey = item.Draft.DedupKey,
        Month = month,
        Fallback = fallback,
        Status = SmsStatus.Queued,
    };

    private string Month() => clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM");

    private static int? LimitOf(TeacherPlan plan) => plan.Has(PlanFeatures.Sms) ? plan.LimitOf(PlanLimits.SmsPerMonth) : 0;

    /// <summary>Parts used this month. Failed messages are not paid for, so they do not count.</summary>
    private Task<int> UsedAsync(string month, Guid? teacherId, CancellationToken ct) =>
        (teacherId is { } id
            ? db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == id)
            : db.Messages)
        .Where(m => m.Month == month && m.Status != SmsStatus.Failed)
        .SumAsync(m => m.Parts, ct);

    /// <summary>True once this number has had a text from GuruBook, whoever sent it: then it needs no "you can stop these" line.</summary>
    private Task<bool> KnownAsync(string phone, CancellationToken ct) =>
        db.Messages.IgnoreQueryFilters().AnyAsync(m => m.ToPhone == phone && m.Status != SmsStatus.Failed, ct);

    private sealed record Ready(SmsDraft Draft, string Name, string Phone, string Text, string Language, int Parts);

    private sealed record Decision(List<Ready> Ready, List<Guid> NoNumber, int OptedOut, int AlreadySent, int Over, int? Limit, int Used);

    private async Task<Decision> DecideAsync(IReadOnlyList<SmsDraft> drafts, CancellationToken ct)
    {
        foreach (var draft in drafts)
        {
            if (!SmsTexts.IsKnownKind(draft.Kind) || draft.Params.Count != WhatsAppKinds.ParamCount(draft.Kind))
            {
                throw new ArgumentException($"A '{draft.Kind}' message needs the values its text takes.", nameof(drafts));
            }
        }

        var plan = await plans.CurrentAsync(ct);
        var limit = LimitOf(plan);
        var month = Month();
        var used = await UsedAsync(month, null, ct);

        var found = await contacts.FindAsync(drafts.Select(d => d.StudentId), ct);
        var stopped = (await db.OptOuts.Select(o => o.Phone).ToListAsync(ct)).ToHashSet();
        var keys = drafts.Select(d => d.DedupKey).Distinct().ToList();
        var sent = (await db.Messages.Where(m => keys.Contains(m.DedupKey) && m.Status != SmsStatus.Failed)
                .Select(m => new { m.DedupKey, m.CreatedAt }).ToListAsync(ct))
            .GroupBy(m => m.DedupKey).ToDictionary(g => g.Key, g => g.Max(m => m.CreatedAt));
        var language = SmsTexts.LanguageOf(tenant.TenantId is { } me ? await teachers.LanguageAsync(me, ct) : "en");
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
                var text = SmsTexts.Compose(draft.Kind, language, draft.Params, known.Add(phone));
                ready.Add(new Ready(draft, contact.FullName, phone, text, language, SmsTexts.Parts(text)));
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
