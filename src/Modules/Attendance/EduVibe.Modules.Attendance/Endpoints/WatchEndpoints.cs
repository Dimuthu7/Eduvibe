using EduVibe.Modules.Attendance.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Attendance.Endpoints;

/// <summary>
/// One student the teacher may want to ring: how many classes in a row they have missed, how often they came lately, and when they last did.
/// </summary>
public sealed record WatchRowDto(
    Guid EnrolmentId, Guid ClassId, string ClassTitle, int IndexNumber, string FullName,
    int MissedInARow, int Sessions, int Percent, DateOnly? LastAttended);

/// <summary>Total is how many students qualify; Rows is the worst few of them.</summary>
public sealed record WatchDto(int WindowDays, int Total, WatchRowDto[] Rows);

/// <summary>
/// The dashboard's attendance watch. It looks at the last 30 days of saved attendance and picks out students who missed their latest two
/// or more classes in a row, or came to fewer than three in four (once they have had at least four classes, so a single early absence is no alarm).
/// </summary>
internal static class WatchEndpoints
{
    public const int WindowDays = 30;
    public const int MinMissedInARow = 2;
    public const int LowPercentBelow = 75;
    public const int MinSessionsForRate = 4;
    public const int MaxRows = 10;

    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/watch", Watch).RequireAuthorization(Policies.Teacher);

    /// <summary>Whether a student's marks, oldest first, are worth flagging, and how many classes they missed at the end.</summary>
    public static (bool Flag, int MissedInARow) Assess(IReadOnlyList<AttendanceStatus> oldestFirst)
    {
        var missed = 0;
        for (var i = oldestFirst.Count - 1; i >= 0 && oldestFirst[i] == AttendanceStatus.Absent; i--)
        {
            missed++;
        }

        var attended = oldestFirst.Count(s => s != AttendanceStatus.Absent);
        var low = oldestFirst.Count >= MinSessionsForRate && SummaryEndpoints.Percent(attended, oldestFirst.Count) < LowPercentBelow;
        return (missed >= MinMissedInARow || low, missed);
    }

    private static async Task<IResult> Watch(
        AttendanceDbContext db, IClassDirectory classes, IClassRoster roster, TimeProvider clock, CancellationToken ct)
    {
        var today = Problems.Today(clock);
        var from = today.AddDays(1 - WindowDays);
        var sessions = await db.Sessions
            .Where(s => s.Date >= from && s.Date <= today && s.TakenAt != null)
            .Select(s => new { s.Id, s.ClassId, s.Date, s.Start })
            .ToListAsync(ct);
        if (sessions.Count == 0)
        {
            return Results.Ok(new WatchDto(WindowDays, 0, []));
        }

        var ids = sessions.Select(s => s.Id).ToList();
        var marks = await db.Marks.Where(m => ids.Contains(m.SessionId)).Select(m => new { m.SessionId, m.EnrolmentId, m.Status }).ToListAsync(ct);
        var byId = sessions.ToDictionary(s => s.Id);
        var titles = await classes.FindManyAsync(sessions.Select(s => s.ClassId).Distinct(), ct);

        var rows = new List<WatchRowDto>();
        foreach (var classId in sessions.Select(s => s.ClassId).Distinct().Where(c => titles.TryGetValue(c, out var t) && !t.IsArchived))
        {
            var inClass = marks.Where(m => byId[m.SessionId].ClassId == classId).GroupBy(m => m.EnrolmentId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var person in await roster.ActiveRosterAsync(classId, ct))
            {
                if (!inClass.TryGetValue(person.EnrolmentId, out var own))
                {
                    continue;
                }

                var ordered = own.OrderBy(m => byId[m.SessionId].Date).ThenBy(m => byId[m.SessionId].Start).ToList();
                var (flag, missed) = Assess([.. ordered.Select(m => m.Status)]);
                if (!flag)
                {
                    continue;
                }

                var attended = ordered.Where(m => m.Status != AttendanceStatus.Absent).ToList();
                rows.Add(new WatchRowDto(
                    person.EnrolmentId, classId, titles[classId].Title, person.IndexNumber, person.FullName, missed, ordered.Count,
                    SummaryEndpoints.Percent(attended.Count, ordered.Count),
                    attended.Count == 0 ? null : attended.Max(m => byId[m.SessionId].Date)));
            }
        }

        var worst = rows
            .OrderByDescending(r => r.MissedInARow).ThenBy(r => r.Percent)
            .ThenBy(r => r.ClassTitle, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.IndexNumber)
            .Take(MaxRows).ToArray();
        return Results.Ok(new WatchDto(WindowDays, rows.Count, worst));
    }
}
