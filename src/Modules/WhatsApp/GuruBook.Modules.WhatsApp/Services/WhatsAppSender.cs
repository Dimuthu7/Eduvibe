using System.Text.Json;
using GuruBook.Modules.WhatsApp.Client;
using GuruBook.Modules.WhatsApp.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Phone;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.WhatsApp.Services;

/// <summary>
/// Decides which drafts can go (a number, not opted out, not sent before, inside the month's allowance) and queues them. The background sender
/// does the actual sending, so the teacher's request returns at once. Preview and send share one decision, so the preview is what send does.
/// </summary>
internal sealed class WhatsAppSender(
    WhatsAppDbContext db, IStudentContacts contacts, IPlanGate plans, IWhatsAppClient client, ISmsSender sms, IMessageSettings messages,
    ITenantContext tenant, WhatsAppSignal signal, WhatsAppDispatchRunner runner, TimeProvider clock) : IWhatsAppSender
{
    public async Task<WhatsAppPreview> PreviewAsync(IReadOnlyList<WhatsAppDraft> drafts, CancellationToken ct)
    {
        // A teacher who chose SMS for everything gets the SMS answer here, so every screen that offers a send needs to know nothing about channels.
        if (await sms.ChannelAsync(ct) == MessageChannels.SmsOnly)
        {
            var text = await sms.PreviewAsync(AsSms(drafts), ct);
            return new WhatsAppPreview(
                text.Configured, text.Total, text.Ready, text.NoNumber, text.OptedOut, text.AlreadySent, text.OverAllowance,
                text.Limit, text.Used, text.NoNumberStudentIds, MessageChannels.SmsOnly);
        }

        var plan = await DecideAsync(drafts, null, ct);
        return new WhatsAppPreview(
            client.IsConfigured, drafts.Count, plan.Ready.Count, plan.NoNumber.Count, plan.OptedOut, plan.AlreadySent, plan.Over,
            plan.Limit, plan.Used, [.. plan.NoNumber]);
    }

    public async Task<WhatsAppSendResult> SendAsync(IReadOnlyList<WhatsAppDraft> drafts, CancellationToken ct)
    {
        if (await sms.ChannelAsync(ct) == MessageChannels.SmsOnly)
        {
            var text = await sms.SendAsync(AsSms(drafts), ct);
            return new WhatsAppSendResult(
                text.Configured, text.Queued, text.NoNumber, text.OptedOut, text.AlreadySent, text.OverAllowance, MessageChannels.SmsOnly);
        }

        return await QueueAsync(drafts, null, ct);
    }

    /// <summary>
    /// For a background job, which acts for nobody: the teacher is named outright and the choice of channel was already made for this event,
    /// so a teacher's own "SMS for everything" setting is not consulted again here.
    /// </summary>
    public Task<WhatsAppSendResult> SendForAsync(Guid teacherId, IReadOnlyList<WhatsAppDraft> drafts, CancellationToken ct = default) =>
        QueueAsync(drafts, teacherId, ct);

    private async Task<WhatsAppSendResult> QueueAsync(IReadOnlyList<WhatsAppDraft> drafts, Guid? teacherId, CancellationToken ct)
    {
        if (!client.IsConfigured)
        {
            return new WhatsAppSendResult(false, 0, 0, 0, 0, 0);
        }

        var plan = await DecideAsync(drafts, teacherId, ct);
        var month = Month();
        var owner = teacherId ?? tenant.TenantId;
        foreach (var item in plan.Ready)
        {
            var name = MessageEvents.IsKnown(item.Draft.Event) ? item.Draft.Event! : MessageEvents.ForKind(item.Draft.Kind);
            db.Messages.Add(new WhatsAppMessage
            {
                TenantId = teacherId ?? Guid.Empty,
                StudentId = item.Draft.StudentId,
                StudentName = item.Name,
                Kind = item.Draft.Kind,
                Event = name,
                Note = owner is { } who ? await messages.NoteForAsync(who, name, ct) : null,
                AllowSms = item.Draft.AllowSms,
                ToPhone = item.Phone,
                ParamsJson = JsonSerializer.Serialize(item.Draft.Params.Select(WhatsAppTemplates.Clean)),
                DedupKey = item.Draft.DedupKey,
                Month = month,
                Status = MessageStatus.Queued,
            });
        }

        if (plan.Ready.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            // Wakes the background loop, and also starts a run straight away in case that loop is not running.
            signal.Wake();
            runner.Kick();
        }

        return new WhatsAppSendResult(true, plan.Ready.Count, plan.NoNumber.Count, plan.OptedOut, plan.AlreadySent, plan.Over);
    }

    /// <summary>The same facts, for the SMS module to put into words.</summary>
    private static List<SmsDraft> AsSms(IReadOnlyList<WhatsAppDraft> drafts) =>
        [.. drafts.Select(d => new SmsDraft(d.StudentId, d.Kind, d.Params, d.DedupKey, d.Window, d.Event))];

    private string Month() => clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM");

    /// <summary>What this teacher has sent: named outright for a background run, which acts for nobody.</summary>
    private IQueryable<WhatsAppMessage> Rows(Guid? teacherId) =>
        teacherId is { } id ? db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == id) : db.Messages;

    private IQueryable<WhatsAppOptOut> OptOuts(Guid? teacherId) =>
        teacherId is { } id ? db.OptOuts.IgnoreQueryFilters().Where(o => o.TenantId == id) : db.OptOuts;

    private sealed record Ready(WhatsAppDraft Draft, string Name, string Phone);

    private sealed record Decision(List<Ready> Ready, List<Guid> NoNumber, int OptedOut, int AlreadySent, int Over, int? Limit, int Used);

    private async Task<Decision> DecideAsync(IReadOnlyList<WhatsAppDraft> drafts, Guid? teacherId, CancellationToken ct)
    {
        foreach (var draft in drafts)
        {
            if (!WhatsAppKinds.IsKnown(draft.Kind) || draft.Params.Count != WhatsAppKinds.ParamCount(draft.Kind))
            {
                throw new ArgumentException($"A '{draft.Kind}' message needs the values its template takes.", nameof(drafts));
            }
        }

        var plan = teacherId is { } owner ? await plans.ForTeacherAsync(owner, ct) : await plans.CurrentAsync(ct);
        int? limit = plan.Has(PlanFeatures.WhatsApp) ? plan.LimitOf(PlanLimits.WhatsAppPerMonth) : 0;
        var month = Month();
        var used = await Rows(teacherId).CountAsync(m => m.Month == month && m.Status != MessageStatus.Failed, ct);

        var ids = drafts.Select(d => d.StudentId);
        var found = teacherId is { } forTeacher
            ? await contacts.FindForTeacherAsync(forTeacher, ids, ct)
            : await contacts.FindAsync(ids, ct);
        var optedOut = (await OptOuts(teacherId).Select(o => o.Phone).ToListAsync(ct)).ToHashSet();
        var keys = drafts.Select(d => d.DedupKey).Distinct().ToList();
        var sent = (await Rows(teacherId).Where(m => keys.Contains(m.DedupKey) && m.Status != MessageStatus.Failed)
                .Select(m => new { m.DedupKey, m.CreatedAt }).ToListAsync(ct))
            .GroupBy(m => m.DedupKey).ToDictionary(g => g.Key, g => g.Max(m => m.CreatedAt));

        var now = clock.GetUtcNow();
        var ready = new List<Ready>();
        var noNumber = new List<Guid>();
        var seen = new HashSet<string>();
        int opted = 0, already = 0;
        foreach (var draft in drafts)
        {
            if (!found.TryGetValue(draft.StudentId, out var contact) || PhoneNumber.Normalize(contact.MessageNumber) is not { } phone)
            {
                noNumber.Add(draft.StudentId);
            }
            else if (optedOut.Contains(phone))
            {
                opted++;
            }
            else if (!seen.Add(draft.DedupKey) || (sent.TryGetValue(draft.DedupKey, out var at) && (draft.Window is null || now - at < draft.Window)))
            {
                already++;
            }
            else
            {
                ready.Add(new Ready(draft, contact.FullName, phone));
            }
        }

        var room = limit is { } cap ? Math.Max(0, cap - used) : int.MaxValue;
        var over = Math.Max(0, ready.Count - room);
        return new Decision(ready.Take(room).ToList(), noNumber, opted, already, over, limit, used);
    }
}
