using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students;

internal sealed class StudentContacts(StudentsDbContext db) : IStudentContacts
{
    // Students are not owned by a teacher, so the teacher's own enrolments decide whose numbers they may use.
    public async Task<IReadOnlyDictionary<Guid, StudentContact>> FindAsync(IEnumerable<Guid> studentIds, CancellationToken cancellationToken = default)
    {
        var ids = studentIds.Distinct().ToList();
        var rows = await db.Enrolments.Where(e => ids.Contains(e.StudentId)).Select(e => e.Student).Distinct().ToListAsync(cancellationToken);
        return rows.ToDictionary(s => s.Id, s => new StudentContact(s.Id, s.FirstName, s.FullName, s.Phone, s.GuardianPhone));
    }
}
