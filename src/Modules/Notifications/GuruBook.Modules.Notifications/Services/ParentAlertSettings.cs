using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Notifications.Services;

/// <summary>What the signed-in teacher's parents are told. A teacher who has never changed it gets the default: every mark.</summary>
internal sealed class ParentAlertSettings(NotificationsDbContext db) : IParentAlertSettings
{
    public async Task<string> AttendanceAlertsAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.ParentAlertSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return row?.AttendanceAlerts is { } choice && AttendanceAlerts.Choices.Contains(choice) ? choice : AttendanceAlerts.All;
    }
}
