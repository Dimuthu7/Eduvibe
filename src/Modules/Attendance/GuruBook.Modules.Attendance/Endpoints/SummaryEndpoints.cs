using GuruBook.Modules.Attendance.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Attendance.Endpoints;

public sealed record StudentSummaryDto(Guid EnrolmentId, int IndexNumber, string FullName, int Sessions, int Present, int Late, int Absent, int Percent);

public sealed record ClassSummaryDto(Guid ClassId, DateOnly From, DateOnly To, int Sessions, int Percent, StudentSummaryDto[] Students);

public sealed record ReportEntryDto(DateOnly Date, TimeOnly Start, TimeOnly End, bool IsExtra, string Status);

public sealed record ReportClassDto(Guid EnrolmentId, Guid ClassId, string Title);

public sealed record StudentReportDto(
    Guid EnrolmentId, Guid StudentId, int IndexNumber, string FullName, Guid ClassId, string ClassTitle, DateOnly From, DateOnly To,
    int Sessions, int Present, int Late, int Absent, int Percent, ReportEntryDto[] Entries, ReportClassDto[] Classes);

public sealed record MyRecentDto(DateOnly Date, string Status);

public sealed record MyAttendanceDto(Guid ClassId, int Sessions, int Present, int Late, int Absent, int Percent, MyRecentDto[] Recent);

internal static class SummaryEndpoints
{
    private const int MaxRangeDays = 366;
    private const int RecentCount = 10;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/summary", Summary).RequireAuthorization(Policies.Teacher);
        group.MapGet("/enrolments/{enrolmentId:guid}/report", StudentReport).RequireAuthorization(Policies.Teacher);
        group.MapGet("/mine", Mine).RequireAuthorization(Policies.Student);
        group.MapGet("/children/{studentId:guid}", Child).RequireAuthorization(Policies.Parent);
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

    // One student in one class over a date range (this month by default): every class that was marked, with how the student came.
    // Classes lists the other classes of the same teacher the student is in, so the screen can switch between them.
    private static async Task<IResult> StudentReport(
        Guid enrolmentId, DateOnly? from, DateOnly? to, AttendanceDbContext db, IClassDirectory classes, IClassRoster roster,
        TimeProvider clock, CancellationToken ct)
    {
        var end = to ?? Problems.Today(clock);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (end < start || end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            return Problems.Reject("range_invalid");
        }

        if (!(await roster.FindEnrolmentsAsync([enrolmentId], ct)).TryGetValue(enrolmentId, out var person))
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        var rows = await db.Marks.Where(m => m.EnrolmentId == enrolmentId)
            .Join(db.Sessions.Where(s => s.TakenAt != null && s.Date >= start && s.Date <= end), m => m.SessionId, s => s.Id,
                (m, s) => new { s.Date, s.Start, s.End, s.IsExtra, m.Status })
            .ToListAsync(ct);
        var entries = rows.OrderBy(r => r.Date).ThenBy(r => r.Start).ToList();

        var own = await roster.EnrolmentsOfStudentAsync(person.StudentId, ct);
        var titles = await classes.FindManyAsync(own.Select(e => e.ClassId), ct);
        var options = own.Where(e => titles.ContainsKey(e.ClassId)).Select(e => new ReportClassDto(e.EnrolmentId, e.ClassId, titles[e.ClassId].Title)).ToArray();

        var present = entries.Count(r => r.Status == AttendanceStatus.Present);
        var late = entries.Count(r => r.Status == AttendanceStatus.Late);
        return Results.Ok(new StudentReportDto(
            person.EnrolmentId, person.StudentId, person.IndexNumber, person.FullName, person.ClassId,
            titles.TryGetValue(person.ClassId, out var cls) ? cls.Title : string.Empty, start, end,
            entries.Count, present, late, entries.Count - present - late, Percent(present + late, entries.Count),
            [.. entries.Select(r => new ReportEntryDto(r.Date, r.Start, r.End, r.IsExtra, r.Status.ToString()))], options));
    }

    // The signed-in student's own attendance in each class they are enrolled in, across teachers.
    private static async Task<IResult> Mine(AttendanceDbContext db, IClassRoster roster, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await Load(db, await roster.ActiveEnrolmentsOfUserAsync(userId, ct), ct));
    }

    // One child's attendance for a parent, in each class whose teacher linked them and has the parent portal.
    private static async Task<IResult> Child(Guid studentId, AttendanceDbContext db, IClassRoster roster, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await Load(db, await roster.ActiveEnrolmentsOfChildAsync(userId, studentId, ct), ct));
    }

    private static async Task<List<MyAttendanceDto>> Load(AttendanceDbContext db, IReadOnlyList<MyEnrolment> enrolments, CancellationToken ct)
    {
        var ids = enrolments.Select(e => e.EnrolmentId).ToList();

        // The reader is not a teacher, so the teacher filter is lifted and the enrolments handed in are the only way in.
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
        return result;
    }
}
