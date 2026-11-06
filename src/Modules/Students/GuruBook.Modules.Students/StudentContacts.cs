using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students;

internal sealed class StudentContacts(StudentsDbContext db) : IStudentContacts
{
    // Students are not owned by a teacher, so the teacher's own enrolments decide whose numbers they may use.
    public Task<IReadOnlyDictionary<Guid, StudentContact>> FindAsync(IEnumerable<Guid> studentIds, CancellationToken cancellationToken = default) =>
        ReadAsync(db.Enrolments, studentIds, cancellationToken);

    // A background job acts for nobody, so the teacher is named outright and their own enrolments still decide.
    public Task<IReadOnlyDictionary<Guid, StudentContact>> FindForTeacherAsync(
        Guid teacherId, IEnumerable<Guid> studentIds, CancellationToken cancellationToken = default) =>
        ReadAsync(db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == teacherId), studentIds, cancellationToken);

    private static async Task<IReadOnlyDictionary<Guid, StudentContact>> ReadAsync(
        IQueryable<Domain.Enrolment> enrolments, IEnumerable<Guid> studentIds, CancellationToken ct)
    {
        var ids = studentIds.Distinct().ToList();
        var rows = await enrolments.Where(e => ids.Contains(e.StudentId)).Select(e => e.Student).Distinct().ToListAsync(ct);
        return rows.ToDictionary(s => s.Id, s => new StudentContact(s.Id, s.FirstName, s.FullName, s.Phone, s.GuardianPhone));
    }
}
