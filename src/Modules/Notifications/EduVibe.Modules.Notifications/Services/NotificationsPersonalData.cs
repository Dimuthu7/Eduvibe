using EduVibe.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Notifications.Services;

/// <summary>
/// A teacher's notifications, which name the students who asked to join. They are addressed to the teacher, not owned by a tenant,
/// so they are handled here rather than by the tenant tables. A student's notifications go when their account is removed.
/// </summary>
internal sealed class NotificationsPersonalData(NotificationsDbContext db) : IPersonalDataSource
{
    public string Module => NotificationsModule.ModuleName;

    public async Task<PersonalData> ExportTeacherAsync(Guid teacherId, CancellationToken ct)
    {
        var rows = await db.Notifications.AsNoTracking().Where(n => n.RecipientId == teacherId).ToListAsync(ct);
        var type = db.Model.FindEntityType(typeof(Domain.Notification))!;
        return rows.Count == 0 ? PersonalData.Empty : new PersonalData { ["notifications"] = [.. rows.Select(r => (object)ModuleData.Project(type, r))] };
    }

    public async Task EraseTeacherAsync(Guid teacherId, CancellationToken ct) =>
        await db.Notifications.Where(n => n.RecipientId == teacherId).ExecuteDeleteAsync(ct);
}
