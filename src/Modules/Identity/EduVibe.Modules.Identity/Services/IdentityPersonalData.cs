using EduVibe.Modules.Identity.Domain;
using EduVibe.Shared.Auditing;
using EduVibe.Shared.Privacy;
using EduVibe.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Identity.Services;

/// <summary>
/// The teacher's account and profile. Erasing a teacher removes the teacher profile and role; when that was the whole account
/// (it is, unless the same person is also a student somewhere) the sign-in account goes too. Runs last, so a failure earlier leaves
/// the teacher there to try again.
/// </summary>
internal sealed class IdentityPersonalData(IdentityDbContext db, AuditDbContext audit) : IPersonalDataSource
{
    public string Module => IdentityModule.ModuleName;

    public int Order => 100;

    public async Task<PersonalData> ExportTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        var teacher = await db.Teachers.Include(t => t.Subjects).AsNoTracking().SingleOrDefaultAsync(t => t.Id == teacherId, ct);
        var data = new PersonalData();
        if (teacher is null)
        {
            return data;
        }

        var user = await db.Users.Include(u => u.Roles).AsNoTracking().SingleAsync(u => u.Id == teacher.UserId, ct);
        data["users"] = [ModuleData.Project(db.Model.FindEntityType(typeof(User))!, user)];
        data["user_roles"] = [.. user.Roles.Select(r => (object)ModuleData.Project(db.Model.FindEntityType(typeof(UserRole))!, r))];
        data["teachers"] = [ModuleData.Project(db.Model.FindEntityType(typeof(Teacher))!, teacher)];
        data["teacher_subjects"] = [.. teacher.Subjects.Select(s => (object)ModuleData.Project(db.Model.FindEntityType(typeof(TeacherSubject))!, s))];
        return data;
    }

    public async Task EraseTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(t => t.Id == teacherId, ct);
        if (teacher is null)
        {
            return;
        }

        // ExecuteDelete skips the audit interceptor, which would otherwise write the person's details into the log as it deletes them.
        await db.TeacherSubjects.Where(s => s.TeacherId == teacherId).ExecuteDeleteAsync(ct);
        await db.Teachers.Where(t => t.Id == teacherId).ExecuteDeleteAsync(ct);
        await db.UserRoles.Where(r => r.UserId == teacher.UserId && r.Role == Roles.Teacher).ExecuteDeleteAsync(ct);
        await AuditRedaction.RedactRecordsAsync(audit, nameof(Teacher), [teacherId], ct);

        if (!await db.UserRoles.AnyAsync(r => r.UserId == teacher.UserId, ct))
        {
            await DeleteUserAsync(teacher.UserId, ct);
        }
    }

    internal async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        await db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        await AuditRedaction.RedactRecordsAsync(audit, nameof(User), [userId], ct);
    }
}
