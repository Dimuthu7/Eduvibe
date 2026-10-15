using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Auditing;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students;

/// <summary>
/// Students are shared between teachers, so this is where who a student is lives. Erasing a teacher deletes their enrolments and
/// invite links, and then the students nobody else teaches. Erasing a student removes who they are (name, phone, school, guardian,
/// sign-in account) and leaves their enrolments, so every teacher's fee, attendance and marks records stay whole, without a name.
/// </summary>
internal sealed class StudentsPersonalData(StudentsDbContext db, IAccountProvisioner accounts, IClassDirectory classes, AuditDbContext audit)
    : TenantTablesSource<StudentsDbContext>(db, StudentsModule.ModuleName), IPersonalDataSource
{
    public override async Task<PersonalData> ExportTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        var data = await base.ExportTeacherAsync(teacherId, ct);
        var ids = await StudentIdsOfAsync(teacherId, ct);
        var students = await Db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (students.Count > 0)
        {
            var type = Db.Model.FindEntityType(typeof(Student))!;
            data["students"] = [.. students.Select(s => (object)ModuleData.Project(type, s))];
        }

        return data;
    }

    public override async Task EraseTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        // Students who were only this teacher's have no reason to stay; students of other teachers keep their record.
        // The orphans are worked out from the enrolments, so those go last, in one transaction with the students: if anything
        // fails the enrolments are still there and running the erasure again finds the same orphans.
        var ids = await StudentIdsOfAsync(teacherId, ct);
        var elsewhere = await Db.Enrolments.IgnoreQueryFilters().Where(e => ids.Contains(e.StudentId) && e.TenantId != teacherId).Select(e => e.StudentId).Distinct().ToListAsync(ct);
        var orphans = ids.Except(elsewhere).ToList();

        var userIds = await Db.Students.IgnoreQueryFilters().Where(s => orphans.Contains(s.Id) && s.UserId != null).Select(s => s.UserId!.Value).ToListAsync(ct);
        foreach (var userId in userIds)
        {
            await accounts.RemoveStudentAccountAsync(userId, ct);
        }

        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        await base.EraseTeacherAsync(teacherId, ct);
        await Db.Students.IgnoreQueryFilters().Where(s => orphans.Contains(s.Id)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        await AuditRedaction.RedactRecordsAsync(audit, nameof(Student), orphans, ct);
    }

    public async Task<PersonalData> ExportStudentAsync(Guid studentId, CancellationToken ct)
    {
        var data = new PersonalData();
        var student = await Db.Students.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null)
        {
            return data;
        }

        data["students"] = [ModuleData.Project(Db.Model.FindEntityType(typeof(Student))!, student)];
        var enrolments = await Db.Enrolments.IgnoreQueryFilters().AsNoTracking().Where(e => e.StudentId == studentId).ToListAsync(ct);
        var titles = await classes.FindManyInAnyTenantAsync(enrolments.Select(e => e.ClassId), ct);
        var type = Db.Model.FindEntityType(typeof(Enrolment))!;
        data["enrolments"] =
        [
            .. enrolments.Select(e =>
            {
                var row = ModuleData.Project(type, e);
                row["classTitle"] = titles.TryGetValue(e.ClassId, out var cls) ? cls.Title : null;
                return (object)row;
            }),
        ];
        return data;
    }

    public async Task EraseStudentAsync(Guid studentId, CancellationToken ct)
    {
        var student = await Db.Students.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null)
        {
            return;
        }

        if (student.UserId is { } userId)
        {
            await accounts.RemoveStudentAccountAsync(userId, ct);
        }

        // The phone number must stay unique and fit its column, so it becomes a marker rather than null.
        var marker = "~" + studentId.ToString("N")[^18..];
        await Db.Students.IgnoreQueryFilters().Where(s => s.Id == studentId).ExecuteUpdateAsync(set => set
            .SetProperty(s => s.Phone, marker)
            .SetProperty(s => s.FirstName, "Deleted")
            .SetProperty(s => s.LastName, "student")
            .SetProperty(s => s.School, (string?)null)
            .SetProperty(s => s.Grade, (string?)null)
            .SetProperty(s => s.GuardianName, (string?)null)
            .SetProperty(s => s.GuardianPhone, (string?)null)
            .SetProperty(s => s.UserId, (Guid?)null), ct);
        await AuditRedaction.RedactRecordsAsync(audit, nameof(Student), [studentId], ct);
    }

    public async Task<string?> FindStudentNameAsync(Guid studentId, CancellationToken ct) =>
        await Db.Students.IgnoreQueryFilters().Where(s => s.Id == studentId).Select(s => s.FirstName + " " + s.LastName).SingleOrDefaultAsync(ct);

    private Task<List<Guid>> StudentIdsOfAsync(Guid teacherId, CancellationToken ct) =>
        Db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == teacherId).Select(e => e.StudentId).Distinct().ToListAsync(ct);
}
