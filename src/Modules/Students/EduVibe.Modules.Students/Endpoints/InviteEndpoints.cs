using System.Security.Cryptography;
using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students.Endpoints;

public sealed record CreateInviteRequest(Guid? ClassId, int? ExpiresInDays, int? MaxUses);

/// <summary>Status is Active, Expired, Full or Revoked. The web app builds the link from Token.</summary>
public sealed record InviteDto(
    Guid Id, Guid ClassId, string ClassTitle, string Token, DateTimeOffset ExpiresAt, int? MaxUses, int UsedCount, string Status, DateTimeOffset CreatedAt);

internal static class InviteEndpoints
{
    public const int DefaultDays = 7;
    public const int MaxDays = 60;
    public const int MaxUsesLimit = 500;

    public static void Map(RouteGroupBuilder group)
    {
        var invites = group.MapGroup("/invites").RequireAuthorization(Policies.Teacher);
        invites.MapGet("", List);
        invites.MapPost("", Create);
        invites.MapPost("/{id:guid}/revoke", Revoke);
    }

    private static async Task<IResult> List(Guid? classId, StudentsDbContext db, IClassDirectory classes, TimeProvider clock, CancellationToken ct)
    {
        var query = db.InviteLinks.AsQueryable();
        if (classId is not null)
        {
            query = query.Where(i => i.ClassId == classId);
        }

        var rows = (await query.ToListAsync(ct)).OrderByDescending(i => i.CreatedAt).Take(100).ToList();
        var titles = await classes.FindManyAsync(rows.Select(r => r.ClassId), ct);
        var now = clock.GetUtcNow();
        return Results.Ok(rows.Select(i => ToDto(i, titles, now)).ToList());
    }

    private static async Task<IResult> Create(CreateInviteRequest request, StudentsDbContext db, IClassDirectory classes, TimeProvider clock, CancellationToken ct)
    {
        var cls = request.ClassId is null ? null : await classes.FindAsync(request.ClassId.Value, ct);
        if (cls is null)
        {
            return Problems.Reject("class_required");
        }

        if (cls.IsArchived)
        {
            return Problems.Reject("class_archived", StatusCodes.Status409Conflict);
        }

        var days = request.ExpiresInDays ?? DefaultDays;
        if (days is < 1 or > MaxDays)
        {
            return Problems.Reject("invite_days_invalid");
        }

        if (request.MaxUses is < 1 or > MaxUsesLimit)
        {
            return Problems.Reject("invite_uses_invalid");
        }

        var now = clock.GetUtcNow();
        var invite = new InviteLink
        {
            ClassId = cls.Id, Token = NewToken(), ExpiresAt = now.AddDays(days), MaxUses = request.MaxUses,
        };
        db.InviteLinks.Add(invite);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/students/invites/{invite.Id}", ToDto(invite, StudentEndpoints.TitlesOf(cls), now));
    }

    private static async Task<IResult> Revoke(Guid id, StudentsDbContext db, IClassDirectory classes, TimeProvider clock, CancellationToken ct)
    {
        var invite = await db.InviteLinks.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (invite is null)
        {
            return Results.NotFound();
        }

        var now = clock.GetUtcNow();
        invite.RevokedAt ??= now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(invite, await classes.FindManyAsync([invite.ClassId], ct), now));
    }

    /// <summary>Why a link cannot be used right now, as a code, or null when it can.</summary>
    public static string? Problem(InviteLink invite, DateTimeOffset now) =>
        invite.RevokedAt is not null ? "invite_revoked"
        : invite.ExpiresAt <= now ? "invite_expired"
        : invite.MaxUses is { } max && invite.UsedCount >= max ? "invite_full"
        : null;

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static InviteDto ToDto(InviteLink i, IReadOnlyDictionary<Guid, ClassSummary> titles, DateTimeOffset now) => new(
        i.Id, i.ClassId, titles.TryGetValue(i.ClassId, out var c) ? c.Title : "", i.Token, i.ExpiresAt, i.MaxUses, i.UsedCount,
        Problem(i, now) switch { "invite_revoked" => "Revoked", "invite_expired" => "Expired", "invite_full" => "Full", _ => "Active" },
        i.CreatedAt);
}
