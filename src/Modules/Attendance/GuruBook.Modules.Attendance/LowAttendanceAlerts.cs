using GuruBook.Modules.Attendance.Domain;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Attendance;

/// <summary>
/// Tells parents when a child is missing too many classes. It is worked out when attendance is saved, over the classes this class actually held
/// in the last few weeks, and only for children marked absent in the session just saved, because nobody else's share can have fallen. A parent
/// hears it at most once a fortnight per class, so a child who keeps missing classes does not turn into a buzzing phone.
/// </summary>
internal static class LowAttendanceAlerts
{
    /// <summary>Below this many classes held, a share means too little to send anything.</summary>
    public const int LeastHeld = 4;

    public static async Task RunAsync(
        ClassSession session, string classTitle, IReadOnlyList<RosterEntry> roll, AttendanceDbContext db, IParentLinks parents,
        INotifier notifier, IMessageRouter messages, ITeacherDirectory teachers, Guid? teacherId, DateOnly today, CancellationToken ct)
    {
        var rule = await messages.RuleAsync(MessageEvents.LowAttendance, ct);
        var level = await messages.LowAttendanceAsync(ct);
        if (!rule.On || roll.Count == 0)
        {
            return;
        }

        var from = today.AddDays(-7 * LowAttendanceRule.Weeks);
        var held = await db.Sessions.Where(s => s.ClassId == session.ClassId && s.TakenAt != null && s.Date >= from && s.Date <= today)
            .Select(s => s.Id).ToListAsync(ct);
        if (held.Count < LeastHeld)
        {
            return;
        }

        var marks = await db.Marks.AsNoTracking().Where(m => held.Contains(m.SessionId)).ToListAsync(ct);
        var attended = marks.Where(m => m.Status != AttendanceStatus.Absent)
            .GroupBy(m => m.EnrolmentId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Only a child marked absent in the session just saved can have crossed the line in this save.
        var missedToday = marks.Where(m => m.SessionId == session.Id && m.Status == AttendanceStatus.Absent).Select(m => m.EnrolmentId).ToHashSet();
        var low = roll.Where(r => missedToday.Contains(r.EnrolmentId))
            .Select(r => (Entry: r, Share: attended.GetValueOrDefault(r.EnrolmentId) * 100 / held.Count))
            .Where(r => r.Share < level.Percent)
            .ToList();
        if (low.Count == 0)
        {
            return;
        }

        var linked = await parents.ParentsOfAsync(low.Select(l => l.Entry.StudentId), ct);

        // The fortnight is counted in whole blocks of days, so the same child's parents are not told twice inside one.
        var window = today.DayNumber / LowAttendanceRule.QuietDays;
        var told = new List<NotifyRequest>();
        foreach (var (entry, share) in low)
        {
            if (!linked.TryGetValue(entry.StudentId, out var people))
            {
                continue;
            }

            var details = new { student = entry.FullName, classTitle, percent = share, weeks = LowAttendanceRule.Weeks, held = held.Count };
            told.AddRange(people.Select(parent => new NotifyRequest(
                parent, "child_low_attendance", details, Link: "/family",
                OnceKey: $"lowatt:{entry.EnrolmentId}:{window}", Topic: AlertTopics.Attendance, AboutStudentId: entry.StudentId)));
        }

        await notifier.NotifyAsync(told, ct);
        if (!MessageWays.IsPaid(rule.Way) || teacherId is not { } me)
        {
            return;
        }

        var teacher = await teachers.FindNameAsync(me, ct) ?? "";
        await messages.SendAsync(
            MessageEvents.LowAttendance,
            [.. low.Select(l => new MessageDraft(
                l.Entry.StudentId,
                [l.Entry.FullName, classTitle, $"{l.Share}%", LowAttendanceRule.Weeks.ToString(), teacher],
                $"{MessageEvents.LowAttendance}:{l.Entry.EnrolmentId}:{window}"))],
            ct);
    }
}
