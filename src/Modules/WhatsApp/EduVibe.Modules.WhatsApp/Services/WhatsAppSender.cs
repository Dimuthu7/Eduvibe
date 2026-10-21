using System.Text.Json;
using EduVibe.Modules.WhatsApp.Client;
using EduVibe.Modules.WhatsApp.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Phone;
using EduVibe.Shared.Plans;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.WhatsApp.Services;

/// <summary>
/// Decides which drafts can go (a number, not opted out, not sent before, inside the month's allowance) and queues them. The background sender
/// does the actual sending, so the teacher's request returns at once. Preview and send share one decision, so the preview is what send does.
/// </summary>
internal sealed class WhatsAppSender(
    WhatsAppDbContext db, IStudentContacts contacts, IPlanGate plans, IWhatsAppClient client,
    WhatsAppSignal signal, TimeProvider clock) : IWhatsAppSender
{
    public async Task<WhatsAppPreview> PreviewAsync(IReadOnlyList<WhatsAppDraft> drafts, CancellationToken ct)
    {
        var plan = await DecideAsync(drafts, ct);
        return new WhatsAppPreview(
            client.IsConfigured, drafts.Count, plan.Ready.Count, plan.NoNumber.Count, plan.OptedOut, plan.AlreadySent, plan.Over,
            plan.Limit, plan.Used, [.. plan.NoNumber]);
    }

    public async Task<WhatsAppSendResult> SendAsync(IReadOnlyList<WhatsAppDraft> drafts, CancellationToken ct)
    {
        if (!client.IsConfigured)
        {
            return new WhatsAppSendResult(false, 0, 0, 0, 0, 0);
        }

        var plan = await DecideAsync(drafts, ct);
        var month = Month();
        foreach (var item in plan.Ready)
        {
            db.Messages.Add(new WhatsAppMessage
            {
                StudentId = item.Draft.StudentId,
                StudentName = item.Name,
                Kind = item.Draft.Kind,
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
            signal.Wake();
        }

        return new WhatsAppSendResult(true, plan.Ready.Count, plan.NoNumber.Count, plan.OptedOut, plan.AlreadySent, plan.Over);
    }

    private string Month() => clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM");

    private sealed record Ready(WhatsAppDraft Draft, string Name, string Phone);

    private sealed record Decision(List<Ready> Ready, List<Guid> NoNumber, int OptedOut, int AlreadySent, int Over, int? Limit, int Used);

    private async Task<Decision> DecideAsync(IReadOnlyList<WhatsAppDraft> drafts, CancellationToken ct)
    {
        foreach (var draft in drafts)
        {
            if (!WhatsAppTemplates.IsKnown(draft.Kind) || draft.Params.Count != WhatsAppTemplates.ParamCount(draft.Kind))
            {
                throw new ArgumentException($"A '{draft.Kind}' message needs the values its template takes.", nameof(drafts));
            }
        }

        var plan = await plans.CurrentAsync(ct);
        int? limit = plan.Has(PlanFeatures.WhatsApp) ? plan.LimitOf(PlanLimits.WhatsAppPerMonth) : 0;
        var month = Month();
        var used = await db.Messages.CountAsync(m => m.Month == month && m.Status != MessageStatus.Failed, ct);

        var found = await contacts.FindAsync(drafts.Select(d => d.StudentId), ct);
        var optedOut = (await db.OptOuts.Select(o => o.Phone).ToListAsync(ct)).ToHashSet();
        var keys = drafts.Select(d => d.DedupKey).Distinct().ToList();
        var sent = (await db.Messages.Where(m => keys.Contains(m.DedupKey) && m.Status != MessageStatus.Failed)
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
