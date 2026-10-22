using System.Text.Json;
using GuruBook.Modules.Notifications.Domain;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Notifications.Endpoints;

/// <summary>Params is the object the sender gave (names, amounts, dates); the app turns Kind and Params into words.</summary>
public sealed record NotificationDto(Guid Id, string Kind, JsonElement Params, string? Link, int Count, bool IsRead, DateTimeOffset At);

public sealed record NotificationListDto(int Unread, NotificationDto[] Items);

public sealed record UnreadDto(int Unread);

/// <summary>
/// The signed-in person's own notifications: a short list of the latest ones, how many are unread, and marking them read.
/// Everything is filtered on the person's own ids (their account, and their teacher id when they are a teacher), so nobody can read
/// or change anybody else's.
/// </summary>
internal static class NotificationEndpoints
{
    public const int Shown = 50;

    public static void Map(RouteGroupBuilder group)
    {
        var mine = group.MapGroup("").RequireAuthorization();
        mine.MapGet("", List);
        mine.MapGet("/unread", Unread);
        mine.MapPost("/{id:guid}/read", Read);
        mine.MapPost("/read-all", ReadAll);
        mine.MapDelete("/{id:guid}", Dismiss);
    }

    private static async Task<IResult> List(NotificationsDbContext db, ITenantContext user, CancellationToken ct)
    {
        if (Mine(user) is not { } mine)
        {
            return Results.Unauthorized();
        }

        // Newest first by id (ids are time ordered), then by the last change, so a notification that grew moves to the top.
        var rows = await db.Notifications.AsNoTracking().Where(n => mine.Contains(n.RecipientId) && n.DismissedAt == null).OrderByDescending(n => n.Id).Take(Shown * 2).ToListAsync(ct);
        var items = rows.OrderByDescending(n => n.UpdatedAt).Take(Shown).Select(ToDto).ToArray();
        return Results.Ok(new NotificationListDto(await UnreadCount(db, mine, ct), items));
    }

    private static async Task<IResult> Unread(NotificationsDbContext db, ITenantContext user, CancellationToken ct) =>
        Mine(user) is { } mine ? Results.Ok(new UnreadDto(await UnreadCount(db, mine, ct))) : Results.Unauthorized();

    private static async Task<IResult> Read(Guid id, NotificationsDbContext db, ITenantContext user, TimeProvider clock, CancellationToken ct)
    {
        if (Mine(user) is not { } mine)
        {
            return Results.Unauthorized();
        }

        var row = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && mine.Contains(n.RecipientId) && n.DismissedAt == null, ct);
        if (row is null)
        {
            return Results.NotFound();
        }

        if (row.ReadAt is null)
        {
            row.ReadAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new UnreadDto(await UnreadCount(db, mine, ct)));
    }

    /// <summary>Closing one removes it from the person's list. It is kept out of sight, not erased, so a repeat of the same reminder stays gone.</summary>
    private static async Task<IResult> Dismiss(Guid id, NotificationsDbContext db, ITenantContext user, TimeProvider clock, CancellationToken ct)
    {
        if (Mine(user) is not { } mine)
        {
            return Results.Unauthorized();
        }

        var row = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && mine.Contains(n.RecipientId) && n.DismissedAt == null, ct);
        if (row is null)
        {
            return Results.NotFound();
        }

        var now = clock.GetUtcNow();
        row.ReadAt ??= now;
        row.DismissedAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new UnreadDto(await UnreadCount(db, mine, ct)));
    }

    private static async Task<IResult> ReadAll(NotificationsDbContext db, ITenantContext user, TimeProvider clock, CancellationToken ct)
    {
        if (Mine(user) is not { } mine)
        {
            return Results.Unauthorized();
        }

        var now = clock.GetUtcNow();
        var open = await db.Notifications.Where(n => mine.Contains(n.RecipientId) && n.DismissedAt == null && n.ReadAt == null).ToListAsync(ct);
        foreach (var row in open)
        {
            row.ReadAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new UnreadDto(0));
    }

    // A student is told by account; a teacher by teacher id, which is the tenant id. Both are the person's own.
    private static List<Guid>? Mine(ITenantContext user) =>
        user.UserId is not { } userId ? null : user.TenantId is { } tenantId ? [userId, tenantId] : [userId];

    private static Task<int> UnreadCount(NotificationsDbContext db, List<Guid> mine, CancellationToken ct) =>
        db.Notifications.CountAsync(n => mine.Contains(n.RecipientId) && n.DismissedAt == null && n.ReadAt == null, ct);

    private static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Kind, JsonDocument.Parse(n.ParamsJson).RootElement.Clone(), n.Link, n.Count, n.ReadAt is not null, n.UpdatedAt);
}
