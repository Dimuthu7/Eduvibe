using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students;

internal sealed class ClassRoster(StudentsDbContext db) : IClassRoster
{
    public async Task<IReadOnlyList<RosterEntry>> ActiveRosterAsync(Guid classId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Enrolments.Include(e => e.Student)
            .Where(e => e.ClassId == classId && e.Status == EnrolmentStatus.Active)
            .OrderBy(e => e.IndexNumber)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToEntry)];
    }

    public async Task<IReadOnlyDictionary<Guid, RosterEntry>> FindEnrolmentsAsync(IEnumerable<Guid> enrolmentIds, CancellationToken cancellationToken = default)
    {
        var ids = enrolmentIds.Distinct().ToList();
        var rows = await db.Enrolments.Include(e => e.Student).Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
        return rows.ToDictionary(e => e.Id, ToEntry);
    }

    // A student sees their own enrolments across teachers, so the teacher filter is lifted and the account is the only way in.
    public async Task<IReadOnlyList<MyEnrolment>> ActiveEnrolmentsOfUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Enrolments.IgnoreQueryFilters()
            .Where(e => e.Status == EnrolmentStatus.Active && e.Student.UserId == userId)
            .Select(e => new MyEnrolment(e.Id, e.StudentId, e.ClassId, e.TenantId))
            .ToListAsync(cancellationToken);
        return rows;
    }

    private static RosterEntry ToEntry(Enrolment e) => new(e.Id, e.StudentId, e.ClassId, e.IndexNumber, e.Student.FullName);
}
