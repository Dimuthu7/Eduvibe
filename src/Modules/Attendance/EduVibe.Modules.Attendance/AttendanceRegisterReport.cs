using System.Globalization;
using EduVibe.Modules.Attendance.Domain;
using EduVibe.Modules.Attendance.Endpoints;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Attendance;

/// <summary>
/// The Excel register of one class over a date range (this month by default): a column per marked session with P, L or A
/// for each student, then their totals and attendance rate. Same rules as the summary screen.
/// </summary>
internal sealed class AttendanceRegisterReport(AttendanceDbContext db, IClassDirectory classes, IClassRoster roster, TimeProvider clock) : IReportSource
{
    private const int MaxRangeDays = 366;

    public string Key => "attendance";

    public async Task<ReportResult> BuildAsync(IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
    {
        if (!query.TryGetValue("classId", out var text) || !Guid.TryParse(text, out var classId) ||
            await classes.FindAsync(classId, cancellationToken) is not { } cls)
        {
            return ReportResult.Fail("class_not_found");
        }

        if (!TryDate(query, "to", out var to) || !TryDate(query, "from", out var from))
        {
            return ReportResult.Fail("range_invalid");
        }

        var end = to ?? Problems.Today(clock);
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (end < start || end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            return ReportResult.Fail("range_invalid");
        }

        var sessions = (await db.Sessions
                .Where(s => s.ClassId == cls.Id && s.Date >= start && s.Date <= end && s.TakenAt != null)
                .ToListAsync(cancellationToken))
            .OrderBy(s => s.Date).ThenBy(s => s.Start).ToList();
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var marks = await db.Marks.Where(m => sessionIds.Contains(m.SessionId)).ToListAsync(cancellationToken);

        var people = (await roster.ActiveRosterAsync(cls.Id, cancellationToken)).ToDictionary(r => r.EnrolmentId);
        var leftIds = marks.Select(m => m.EnrolmentId).Distinct().Where(e => !people.ContainsKey(e)).ToList();
        if (leftIds.Count > 0)
        {
            foreach (var (enrolmentId, entry) in await roster.FindEnrolmentsAsync(leftIds, cancellationToken))
            {
                people[enrolmentId] = entry;
            }
        }

        var byStudent = marks.GroupBy(m => m.EnrolmentId).ToDictionary(g => g.Key, g => g.ToDictionary(m => m.SessionId, m => m.Status));
        var rows = people.Values.OrderBy(p => p.IndexNumber).Select(p =>
        {
            var own = byStudent.GetValueOrDefault(p.EnrolmentId) ?? [];
            var present = own.Values.Count(s => s == AttendanceStatus.Present);
            var late = own.Values.Count(s => s == AttendanceStatus.Late);
            return (IReadOnlyList<object?>)
            [
                p.IndexNumber, p.FullName,
                .. sessions.Select(s => own.TryGetValue(s.Id, out var status) ? Letter(status) : null),
                own.Count, present, late, own.Count - present - late, SummaryEndpoints.Percent(present + late, own.Count),
            ];
        }).ToList();

        var sameDay = sessions.GroupBy(s => s.Date).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var headings = sessions.Select(s =>
            s.Date.ToString("d MMM", CultureInfo.InvariantCulture)
            + (sameDay.Contains(s.Date) ? " " + s.Start.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty)
            + (s.IsExtra ? " (extra)" : string.Empty));
        return ReportResult.Ok(new ReportTable(
            $"Attendance {cls.Title} {start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            ["Index", "Student", .. headings, "Sessions", "Present", "Late", "Absent", "Attendance %"],
            rows));
    }

    private static string Letter(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "P",
        AttendanceStatus.Late => "L",
        _ => "A",
    };

    /// <summary>False when the value is there but is not a date; a missing value is fine and leaves the date null.</summary>
    private static bool TryDate(IReadOnlyDictionary<string, string> query, string key, out DateOnly? date)
    {
        date = null;
        if (!query.TryGetValue(key, out var text) || text.Length == 0)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        date = parsed;
        return true;
    }
}
