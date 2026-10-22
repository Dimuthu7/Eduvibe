using GuruBook.Modules.Classes.Domain;
using GuruBook.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Classes;

/// <summary>
/// A teacher's classes, timetable and venues. Institutes are shared between teachers and stay; only the teacher's link to them goes.
/// </summary>
internal sealed class ClassesPersonalData(ClassesDbContext db) : TenantTablesSource<ClassesDbContext>(db, ClassesModule.ModuleName)
{
    public override async Task<PersonalData> ExportTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        var data = await base.ExportTeacherAsync(teacherId, ct);
        var links = await Db.InstituteTeachers.Where(l => l.TeacherId == teacherId).ToListAsync(ct);
        if (links.Count > 0)
        {
            var type = Db.Model.FindEntityType(typeof(InstituteTeacher))!;
            data["institute_teachers"] = [.. links.Select(l => (object)ModuleData.Project(type, l))];
        }

        return data;
    }

    public override async Task EraseTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        await base.EraseTeacherAsync(teacherId, ct);
        await Db.InstituteTeachers.Where(l => l.TeacherId == teacherId).ExecuteDeleteAsync(ct);
    }
}
