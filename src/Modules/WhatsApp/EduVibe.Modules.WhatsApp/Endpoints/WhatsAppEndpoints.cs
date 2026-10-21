using EduVibe.Modules.WhatsApp.Client;
using EduVibe.Modules.WhatsApp.Domain;
using EduVibe.Shared.Plans;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduVibe.Modules.WhatsApp.Endpoints;

/// <summary>
/// What the app needs to decide how to show WhatsApp. Enabled is whether the teacher's plan includes it (false shows the locked state);
/// Configured is whether this server is set up to send; Simulated means it only pretends (a demo). Limit is null for no cap.
/// </summary>
public sealed record WhatsAppStatusDto(bool Enabled, bool Configured, bool Simulated, int? Limit, int Used, int? Remaining);

public sealed record WhatsAppMessageDto(
    Guid Id, string StudentName, string Kind, string Status, string? Error, string Phone, DateTimeOffset CreatedAt);

public sealed record WhatsAppListDto(WhatsAppStatusDto Status, WhatsAppMessageDto[] Items);

/// <summary>The teacher's own WhatsApp: whether it is available to them and what has been sent. Sending itself starts from fees, attendance and exams.</summary>
internal static class WhatsAppEndpoints
{
    public const int Shown = 100;

    public static void Map(RouteGroupBuilder group)
    {
        // Status is open to every teacher so a Free teacher is shown the locked state instead of an error.
        group.MapGet("/status", Status).RequireAuthorization(Policies.Teacher);
        group.MapGet("/messages", Messages).RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.WhatsApp);
    }

    private static async Task<IResult> Status(WhatsAppDbContext db, IPlanGate plans, IWhatsAppClient client, IOptions<WhatsAppOptions> options, TimeProvider clock, CancellationToken ct) =>
        Results.Ok(await StatusAsync(db, plans, client, options.Value, clock, ct));

    internal static async Task<WhatsAppStatusDto> StatusAsync(WhatsAppDbContext db, IPlanGate plans, IWhatsAppClient client, WhatsAppOptions options, TimeProvider clock, CancellationToken ct)
    {
        var plan = await plans.CurrentAsync(ct);
        var enabled = plan.Has(PlanFeatures.WhatsApp);
        int? limit = enabled ? plan.LimitOf(PlanLimits.WhatsAppPerMonth) : 0;
        var month = clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM");
        var used = await db.Messages.CountAsync(m => m.Month == month && m.Status != MessageStatus.Failed, ct);
        return new WhatsAppStatusDto(enabled, client.IsConfigured, options.IsSimulated && client.IsConfigured, limit, used, limit is { } cap ? Math.Max(0, cap - used) : null);
    }

    // Newest first. The id is time-ordered, so it sorts like the date and works on every database.
    private static async Task<IResult> Messages(string? kind, WhatsAppDbContext db, IPlanGate plans, IWhatsAppClient client, IOptions<WhatsAppOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var query = db.Messages.AsNoTracking().AsQueryable();
        if (kind is not null)
        {
            query = query.Where(m => m.Kind == kind);
        }

        var rows = await query.OrderByDescending(m => m.Id).Take(Shown).ToListAsync(ct);
        return Results.Ok(new WhatsAppListDto(
            await StatusAsync(db, plans, client, options.Value, clock, ct),
            [.. rows.Select(m => new WhatsAppMessageDto(m.Id, m.StudentName, m.Kind, m.Status.ToString(), m.Error, Mask(m.ToPhone), m.CreatedAt))]));
    }

    // The list shows only the end of the number: enough to tell parents apart, not enough to copy.
    private static string Mask(string phone) => phone.Length <= 4 ? phone : "•••• " + phone[^4..];
}
