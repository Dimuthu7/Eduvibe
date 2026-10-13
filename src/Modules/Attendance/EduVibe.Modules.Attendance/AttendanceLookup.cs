using EduVibe.Modules.Attendance.Domain;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Attendance;

internal sealed class AttendanceLookup(AttendanceDbContext db) : IAttendanceLookup
{
    public async Task<IReadOnlyDictionary<Guid, int>> AttendedCountsAsync(DateOnly from, DateOnly to, Guid? classId, CancellationToken cancellationToken = default)
    {
        var sessions = db.Sessions.Where(s => s.TakenAt != null && s.Date >= from && s.Date <= to);
        if (classId is not null)
        {
            sessions = sessions.Where(s => s.ClassId == classId);
        }

        // Counted here rather than in SQL so it also runs on the SQLite database the tests use.
        var attended = await db.Marks
            .Where(m => m.Status != AttendanceStatus.Absent)
            .Join(sessions, m => m.SessionId, s => s.Id, (m, _) => m.EnrolmentId)
            .ToListAsync(cancellationToken);
        return attended.GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
    }
}
