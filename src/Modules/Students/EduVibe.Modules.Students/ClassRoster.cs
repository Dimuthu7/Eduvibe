using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Plans;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students;

internal sealed class ClassRoster(StudentsDbContext db, IPlanGate plans) : IClassRoster
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

    public async Task<IReadOnlyList<RosterEntry>> EnrolmentsOfStudentAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Enrolments.Include(e => e.Student).Where(e => e.StudentId == studentId).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(e => e.JoinedOn).Select(ToEntry)];
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

    // A parent sees a child only through a link a teacher made, so the links decide, and only while that teacher's plan has the parent portal.
    public async Task<IReadOnlyList<MyEnrolment>> ActiveEnrolmentsOfChildAsync(Guid parentUserId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var linkedTenants = await db.ParentLinks.IgnoreQueryFilters()
            .Where(l => l.ParentUserId == parentUserId && l.StudentId == studentId)
            .Select(l => l.TenantId).ToListAsync(cancellationToken);
        var rows = await db.Enrolments.IgnoreQueryFilters()
            .Where(e => e.StudentId == studentId && e.Status == EnrolmentStatus.Active && linkedTenants.Contains(e.TenantId))
            .Select(e => new MyEnrolment(e.Id, e.StudentId, e.ClassId, e.TenantId))
            .ToListAsync(cancellationToken);

        var allowed = new List<MyEnrolment>();
        foreach (var row in rows)
        {
            if ((await plans.ForTeacherAsync(row.TenantId, cancellationToken)).Has(PlanFeatures.ParentPortal))
            {
                allowed.Add(row);
            }
        }

        return allowed;
    }

    public async Task<IReadOnlyList<FeeBasis>> FeeBasisAsync(Guid? classId, CancellationToken cancellationToken = default)
    {
        var query = db.Enrolments.Include(e => e.Student).Where(e => e.Status == EnrolmentStatus.Active);
        if (classId is not null)
        {
            query = query.Where(e => e.ClassId == classId);
        }

        return [.. (await query.OrderBy(e => e.IndexNumber).ToListAsync(cancellationToken)).Select(ToBasis)];
    }

    public async Task<FeeBasis?> FindFeeBasisAsync(Guid enrolmentId, CancellationToken cancellationToken = default)
    {
        var row = await db.Enrolments.Include(e => e.Student).SingleOrDefaultAsync(e => e.Id == enrolmentId, cancellationToken);
        return row is null ? null : ToBasis(row);
    }

    // A background job acts for no teacher, so the teacher filter is lifted and every active enrolment is returned.
    public async Task<IReadOnlyList<FeeBasis>> FeeBasisForAllTeachersAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Enrolments.IgnoreQueryFilters().Include(e => e.Student)
            .Where(e => e.Status == EnrolmentStatus.Active)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToBasis)];
    }

    private static FeeBasis ToBasis(Enrolment e) => new(
        e.Id, e.StudentId, e.TenantId, e.ClassId, e.Student.FullName, e.IndexNumber,
        e.FeePlan.ToString(), e.DiscountAmount, e.JoinedOn, e.Status == EnrolmentStatus.Active, e.Student.UserId);

    private static RosterEntry ToEntry(Enrolment e) => new(e.Id, e.StudentId, e.ClassId, e.IndexNumber, e.Student.FullName, e.Student.UserId);
}
