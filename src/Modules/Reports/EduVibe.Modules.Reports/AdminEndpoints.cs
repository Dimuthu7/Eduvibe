using EduVibe.Shared.Auditing;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Reports;

/// <summary>One change in the audit log. Before and After are JSON of the fields that changed (all fields for a create or delete).</summary>
public sealed record AuditRowDto(
    Guid Id, DateTimeOffset At, string Entity, Guid EntityId, string Action, Guid? UserId, string? UserName, Guid? TenantId, string? Before, string? After);

public sealed record AuditPageDto(AuditRowDto[] Items, bool HasMore);

/// <summary>What the Super Admin sees across all teachers: the dashboard numbers and the audit log.</summary>
internal static class AdminEndpoints
{
    private const int PageSize = 50;
    private static readonly TimeSpan Colombo = TimeSpan.FromMinutes(330);

    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("").RequireAuthorization(Policies.SuperAdmin);
        admin.MapGet("/dashboard", Dashboard);
        admin.MapGet("/audit", Audit);
        admin.MapGet("/audit/entities", AuditEntities);
    }

    // Each module adds its own numbers; this only gathers them.
    private static async Task<IResult> Dashboard(IEnumerable<IStatsSource> sources, CancellationToken ct)
    {
        var stats = new List<Stat>();
        foreach (var source in sources)
        {
            stats.AddRange(await source.ReadAsync(ct));
        }

        return Results.Ok(stats);
    }

    // Newest first. Ids are time-ordered, so paging by id is paging by time. Dates are Sri Lanka days; PostgreSQL takes them as UTC.
    private static async Task<IResult> Audit(
        string? entity, string? action, Guid? teacherId, Guid? userId, DateOnly? from, DateOnly? to, int? skip,
        AuditDbContext db, IUserDirectory users, CancellationToken ct)
    {
        var query = db.Entries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entity))
        {
            query = query.Where(e => e.Entity == entity);
        }

        if (Enum.TryParse<AuditAction>(action, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            query = query.Where(e => e.Action == parsed);
        }

        if (teacherId is { } tenant)
        {
            query = query.Where(e => e.TenantId == tenant);
        }

        if (userId is { } user)
        {
            query = query.Where(e => e.UserId == user);
        }

        if (from is { } start)
        {
            var at = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), Colombo).ToUniversalTime();
            query = query.Where(e => e.At >= at);
        }

        if (to is { } end)
        {
            var at = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), Colombo).ToUniversalTime();
            query = query.Where(e => e.At < at);
        }

        var rows = await query.OrderByDescending(e => e.Id).Skip(Math.Max(skip ?? 0, 0)).Take(PageSize + 1).ToListAsync(ct);
        var names = await users.NamesAsync(rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value), ct);
        return Results.Ok(new AuditPageDto(
            [.. rows.Take(PageSize).Select(r => new AuditRowDto(
                r.Id, r.At, r.Entity, r.EntityId, r.Action.ToString().ToLowerInvariant(), r.UserId,
                r.UserId is { } id && names.TryGetValue(id, out var name) ? name : null, r.TenantId, r.Before, r.After))],
            rows.Count > PageSize));
    }

    private static async Task<IResult> AuditEntities(AuditDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Entries.Select(e => e.Entity).Distinct().OrderBy(e => e).ToListAsync(ct));
}
