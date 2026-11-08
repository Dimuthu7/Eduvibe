using GuruBook.Shared.Modules;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Messaging.Services;

/// <summary>
/// Sends an event's paid messages the way the teacher chose. The modules that know who should be told hand over the facts and nothing else:
/// whether anything is paid for, and whether it goes by WhatsApp or by text message, is decided here.
/// </summary>
internal sealed class MessageRouter(IMessageSettings settings, IWhatsAppSender whatsApp, ISmsSender sms, ITenantContext tenant) : IMessageRouter
{
    public Task<MessageRule> RuleAsync(string messageEvent, CancellationToken ct = default) => settings.RuleAsync(messageEvent, ct);

    public Task<MessageRule> RuleForAsync(Guid teacherId, string messageEvent, CancellationToken ct = default) =>
        settings.RuleForAsync(teacherId, messageEvent, ct);

    public Task<LowAttendanceRule> LowAttendanceAsync(CancellationToken ct = default) =>
        tenant.TenantId is { } me ? settings.LowAttendanceForAsync(me, ct) : Task.FromResult(LowAttendanceRule.Default);

    public async Task<int> SendAsync(string messageEvent, IReadOnlyList<MessageDraft> drafts, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } me)
        {
            return 0;
        }

        var rule = await settings.RuleForAsync(me, messageEvent, ct);
        if (Plan(messageEvent, rule, drafts) is not { } plan)
        {
            return 0;
        }

        return plan.ByWhatsApp
            ? (await whatsApp.SendAsync(plan.WhatsApp, ct)).Queued
            : (await sms.SendAsync(plan.Sms, ct)).Queued;
    }

    public async Task<int> SendForAsync(Guid teacherId, string messageEvent, IReadOnlyList<MessageDraft> drafts, CancellationToken ct = default)
    {
        var rule = await settings.RuleForAsync(teacherId, messageEvent, ct);
        if (Plan(messageEvent, rule, drafts) is not { } plan)
        {
            return 0;
        }

        return plan.ByWhatsApp
            ? (await whatsApp.SendForAsync(teacherId, plan.WhatsApp, ct)).Queued
            : (await sms.SendForAsync(teacherId, plan.Sms, ct)).Queued;
    }

    private sealed record Sending(bool ByWhatsApp, List<WhatsAppDraft> WhatsApp, List<SmsDraft> Sms);

    /// <summary>
    /// What to send, or null when nothing is paid for. An event Meta has approved no template for goes by text message even when the teacher
    /// asked for WhatsApp, because the choice they made was to pay for it to reach a phone.
    /// </summary>
    private static Sending? Plan(string messageEvent, MessageRule rule, IReadOnlyList<MessageDraft> drafts)
    {
        if (!rule.On || !MessageWays.IsPaid(rule.Way) || drafts.Count == 0 || !MessageEvents.IsKnown(messageEvent))
        {
            return null;
        }

        var info = MessageEvents.Of(messageEvent);
        foreach (var draft in drafts)
        {
            if (draft.Params.Count != info.Fields.Count)
            {
                throw new ArgumentException($"A '{messageEvent}' message needs the values its fields take.", nameof(drafts));
            }
        }

        var byWhatsApp = MessageWays.UsesWhatsApp(rule.Way) && info.WhatsAppKind is not null;
        return new Sending(
            byWhatsApp,
            byWhatsApp
                ? [.. drafts.Select(d => new WhatsAppDraft(
                    d.StudentId, info.WhatsAppKind!, d.Params, d.DedupKey, d.Window, messageEvent, rule.Way == MessageWays.WhatsAppThenSms))]
                : [],
            byWhatsApp ? [] : [.. drafts.Select(d => new SmsDraft(d.StudentId, KindForSms(info), d.Params, d.DedupKey, d.Window, messageEvent))]);
    }

    /// <summary>The SMS module writes from the event, but a draft still carries a kind, so an event with no template borrows a harmless one.</summary>
    private static string KindForSms(MessageEvents.Info info) => info.WhatsAppKind ?? WhatsAppKinds.FeeReminder;
}
