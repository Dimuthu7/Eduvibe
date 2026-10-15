using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Shared.Privacy;

/// <summary>
/// A personal data source for a module whose personal data is all tenant-owned rows: a teacher's export is every row with their
/// tenant id and erasing them is deleting those rows. The module registers a subclass and adds nothing else.
/// </summary>
public abstract class TenantTablesSource<TContext>(TContext db, string module) : IPersonalDataSource where TContext : DbContext
{
    protected TContext Db { get; } = db;

    public string Module { get; } = module;

    public virtual Task<PersonalData> ExportTeacherAsync(Guid teacherId, CancellationToken ct) => ModuleData.ReadTenantAsync(Db, teacherId, ct);

    public virtual Task EraseTeacherAsync(Guid teacherId, CancellationToken ct) => ModuleData.EraseTenantAsync(Db, teacherId, ct);
}

/// <summary>
/// As <see cref="TenantTablesSource{TContext}"/>, for a module whose rows hang off enrolments (attendance, fees, marks): a student's
/// export is the rows of their enrolments, with every teacher. Erasing a student needs nothing here: the rows only name an enrolment,
/// and the student behind it is anonymised by the Students module.
/// </summary>
public abstract class EnrolmentTablesSource<TContext>(TContext db, string module, IClassRoster roster) : TenantTablesSource<TContext>(db, module), IPersonalDataSource
    where TContext : DbContext
{
    public async Task<PersonalData> ExportStudentAsync(Guid studentId, CancellationToken ct)
    {
        var enrolments = await roster.EnrolmentsOfStudentAsync(studentId, ct);
        return await ModuleData.ReadByEnrolmentsAsync(Db, [.. enrolments.Select(e => e.EnrolmentId)], ct);
    }
}
