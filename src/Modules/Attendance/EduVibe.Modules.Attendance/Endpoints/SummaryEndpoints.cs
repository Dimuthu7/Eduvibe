using EduVibe.Modules.Attendance.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Attendance.Endpoints;

public sealed record StudentSummaryDto(Guid EnrolmentId, int IndexNumber, string FullName, int Sessions, int Present, int Late, int Absent, int Percent);

public sealed record ClassSummaryDto(Guid ClassId, DateOnly From, DateOnly To, int Sessions, int Percent, StudentSummaryDto[] Students);

public sealed record MyRecentDto(DateOnly Date, string Status);

public sealed record MyAttendanceDto(Guid ClassId, int Sessions, int Present, int Late, int Absent, int Percent, MyRecentDto[] Recent);

internal static class SummaryEndpoints
{
    private const int MaxRangeDays = 366;
    private const int RecentCount = 10;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/summary", Summary).RequireAuthorization(Policies.Teacher);
        group.MapGet("/mine", Mine).RequireAuthorization(Policies.Student);
    }

    /// <summary>Late counts as attended. The percentage is of the sessions that were actually marked.</summary>
    public static int Percent(int attended, int total) => total == 0 ? 0 : (int)Math.Round(100.0 * attended / total, MidpointRounding.AwayFromZero);

    // One class over a date range (this month by default): each student's present, late and absent counts and attendance rate.
    private static async Task<IResult> Summary(
        Guid? classId, DateOnly? from, DateOnly? to, AttendanceDbContext db, IClassDirectory classes, IClassRoster roster,
        TimeProvider clock, CancellationToken ct)
    {
        var today = Problems.Today(clock);
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (end < start || end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            return Problems.Reject("range_invalid");
        }

        var cls = classId is { } id ? await classes.FindAsync(id, ct) : null;
        if (cls is null)
        {
            return Problems.Reject("class_not_found", StatusCodes.Status404NotFound);
        }

        var sessionIds = await db.Sessions
            .Where(s => s.ClassId == cls.Id && s.Date >= start && s.Date <= end && s.TakenAt != null)
            .Select(s => s.Id).ToListAsync(ct);
        var marks = await db.Marks.Where(m => sessionIds.Contains(m.SessionId)).Select(m => new { m.EnrolmentId, m.Status }).ToListAsync(ct);

        var people = (await roster.ActiveRosterAsync(cls.Id, ct)).ToDictionary(r => r.EnrolmentId);
        var leftIds = marks.Select(m => m.EnrolmentId).Distinct().Where(e => !people.ContainsKey(e)).ToList();
        if (leftIds.Count > 0)
        {
            foreach (var (enrolmentId, entry) in await roster.FindEnrolmentsAsync(leftIds, ct))
            {
                people[enrolmentId] = entry;
            }
        }

        var byStudent = marks.GroupBy(m => m.EnrolmentId).ToDictionary(g => g.Key, g => g.ToList());
        var students = people.Values.OrderBy(p => p.IndexNumber).Select(p =>
        {
            var own = byStudent.TryGetValue(p.EnrolmentId, out var found) ? found : [];
            var present = own.Count(m => m.Status == AttendanceStatus.Present);
            var late = own.Count(m => m.Status == AttendanceStatus.Late);
            return new StudentSummaryDto(p.EnrolmentId, p.IndexNumber, p.FullName, own.Count, present, late, own.Count - present - late, Percent(present + late, own.Count));
        }).ToArray();

        var attended = marks.Count(m => m.Status != AttendanceStatus.Absent);
        return Results.Ok(new ClassSummaryDto(cls.Id, start, end, sessionIds.Count, Percent(attended, marks.Count), students));
    }

    // The signed-in student's own attendance in each class they are enrolled in, across teachers.
    private static async Task<IResult> Mine(AttendanceDbContext db, IClassRoster roster, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var enrolments = await roster.ActiveEnrolmentsOfUserAsync(userId, ct);
        var ids = enrolments.Select(e => e.EnrolmentId).ToList();

        // The student is not a teacher, so the teacher filter is lifted and the student's own enrolments are the only way in.
        var rows = await db.Marks.IgnoreQueryFilters()
            .Where(m => ids.Contains(m.EnrolmentId))
            .Join(db.Sessions.IgnoreQueryFilters().Where(s => s.TakenAt != null), m => m.SessionId, s => s.Id,
                (m, s) => new { m.EnrolmentId, m.Status, s.Date })
            .ToListAsync(ct);

        var result = enrolments.Select(e =>
        {
            var own = rows.Where(r => r.EnrolmentId == e.EnrolmentId).OrderByDescending(r => r.Date).ToList();
            var present = own.Count(r => r.Status == AttendanceStatus.Present);
            var late = own.Count(r => r.Status == AttendanceStatus.Late);
            return new MyAttendanceDto(
                e.ClassId, own.Count, present, late, own.Count - present - late, Percent(present + late, own.Count),
                [.. own.Take(RecentCount).Select(r => new MyRecentDto(r.Date, r.Status.ToString()))]);
        }).ToList();
        return Results.Ok(result);
    }
}
