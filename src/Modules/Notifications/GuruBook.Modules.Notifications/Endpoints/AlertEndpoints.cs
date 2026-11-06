using GuruBook.Modules.Notifications.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Notifications.Endpoints;

/// <summary>One silence the signed-in person has set: this topic, about this child.</summary>
public sealed record MutedTopicDto(Guid StudentId, string Topic);

public sealed record MyAlertsDto(MutedTopicDto[] Muted);

/// <summary>Switching one topic about one child on or off. On is the default, so nothing is stored while everything is on.</summary>
public sealed record MuteRequest(Guid? StudentId, string? Topic, bool? On);

/// <summary>
/// Who hears what. A parent switches a topic off for one child, for every teacher that child learns with, because it is their phone.
/// What a teacher sends at all, and how, is on their own Messages page.
/// </summary>
internal static class AlertEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var mine = group.MapGroup("/alerts").RequireAuthorization();
        mine.MapGet("", Mine);
        mine.MapPut("", Mute);
    }

    private static async Task<IResult> Mine(NotificationsDbContext db, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var rows = await db.AlertMutes.AsNoTracking().Where(m => m.UserId == userId)
            .Select(m => new MutedTopicDto(m.StudentId, m.Topic)).ToArrayAsync(ct);
        return Results.Ok(new MyAlertsDto(rows));
    }

    private static async Task<IResult> Mute(MuteRequest request, NotificationsDbContext db, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (request.StudentId is not { } studentId || !AlertTopics.IsKnown(request.Topic) || request.On is not { } on)
        {
            return Problems.Reject("alert_topic_invalid");
        }

        var topic = request.Topic!;
        var row = await db.AlertMutes.SingleOrDefaultAsync(m => m.UserId == userId && m.StudentId == studentId && m.Topic == topic, ct);
        if (on && row is not null)
        {
            db.AlertMutes.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        else if (!on && row is null)
        {
            db.AlertMutes.Add(new AlertMute { UserId = userId, StudentId = studentId, Topic = topic });
            await db.SaveChangesAsync(ct);
        }

        return await Mine(db, user, ct);
    }
}
