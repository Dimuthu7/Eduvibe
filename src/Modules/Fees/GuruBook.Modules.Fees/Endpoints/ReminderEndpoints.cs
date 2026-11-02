using GuruBook.Modules.Fees.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Endpoints;

/// <summary>How reminders work for a class, and which of its students are skipped.</summary>
public sealed record ReminderSettingsDto(bool Enabled, int? WeeklyLimit, Guid[] SkippedEnrolments);

public sealed record ReminderSettingsRequest(bool? Enabled, int? WeeklyLimit);

public sealed record SkipRequest(bool? Skip);

/// <summary>One reminder that went out for a fee: which step, which week for a late one, when, and how.</summary>
public sealed record ReminderDto(string Step, int Number, string Month, DateTimeOffset At, string Channel);

/// <summary>The teacher's say over automatic fee reminders: per class, and per student.</summary>
internal static class ReminderEndpoints
{
    public const int MaxWeeklyLimit = 8;

    public static void Map(RouteGroupBuilder group)
    {
        var reminders = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        reminders.MapGet("/classes/{classId:guid}/reminders", Get);
        reminders.MapPut("/classes/{classId:guid}/reminders", Save);
        reminders.MapPut("/enrolments/{enrolmentId:guid}/reminders", Skip);
    }

    private static async Task<IResult> Get(Guid classId, FeesDbContext db, IClassDirectory classes, IClassRoster roster, CancellationToken ct)
    {
        if (await classes.FindAsync(classId, ct) is null)
        {
            return Problems.Reject("class_not_found", StatusCodes.Status404NotFound);
        }

        return Results.Ok(await Read(classId, db, roster, ct));
    }

    private static async Task<IResult> Save(
        Guid classId, ReminderSettingsRequest request, FeesDbContext db, IClassDirectory classes, IClassRoster roster, ITenantContext user, CancellationToken ct)
    {
        if (await classes.FindAsync(classId, ct) is null || user.TenantId is not { } tenantId)
        {
            return Problems.Reject("class_not_found", StatusCodes.Status404NotFound);
        }

        if (request.Enabled is null || request.WeeklyLimit is < 1 or > MaxWeeklyLimit)
        {
            return Problems.Reject("weekly_limit_invalid");
        }

        var row = await db.ReminderSettings.SingleOrDefaultAsync(s => s.ClassId == classId, ct);
        if (row is null)
        {
            row = new ClassReminderSetting { TenantId = tenantId, ClassId = classId };
            db.ReminderSettings.Add(row);
        }

        row.Enabled = request.Enabled.Value;
        row.WeeklyLimit = request.WeeklyLimit;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Read(classId, db, roster, ct));
    }

    private static async Task<IResult> Skip(Guid enrolmentId, SkipRequest request, FeesDbContext db, IClassRoster roster, ITenantContext user, CancellationToken ct)
    {
        if (await roster.FindFeeBasisAsync(enrolmentId, ct) is null || user.TenantId is not { } tenantId)
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        if (request.Skip is not { } skip)
        {
            return Problems.Reject("invalid_request");
        }

        var row = await db.ReminderSkips.SingleOrDefaultAsync(s => s.EnrolmentId == enrolmentId, ct);
        if (skip && row is null)
        {
            db.ReminderSkips.Add(new ReminderSkip { TenantId = tenantId, EnrolmentId = enrolmentId });
            await db.SaveChangesAsync(ct);
        }
        else if (!skip && row is not null)
        {
            db.ReminderSkips.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<ReminderSettingsDto> Read(Guid classId, FeesDbContext db, IClassRoster roster, CancellationToken ct)
    {
        var setting = await db.ReminderSettings.AsNoTracking().SingleOrDefaultAsync(s => s.ClassId == classId, ct);
        var enrolments = (await roster.FeeBasisAsync(classId, ct)).Select(b => b.EnrolmentId).ToList();
        var skipped = await db.ReminderSkips.AsNoTracking().Where(s => enrolments.Contains(s.EnrolmentId)).Select(s => s.EnrolmentId).ToListAsync(ct);
        return new ReminderSettingsDto(setting?.Enabled ?? true, setting?.WeeklyLimit, [.. skipped]);
    }
}
