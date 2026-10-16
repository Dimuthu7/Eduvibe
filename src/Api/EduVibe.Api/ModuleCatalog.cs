using EduVibe.Modules.Attendance;
using EduVibe.Modules.Catalog;
using EduVibe.Modules.Classes;
using EduVibe.Modules.Dashboard;
using EduVibe.Modules.Exams;
using EduVibe.Modules.Fees;
using EduVibe.Modules.Identity;
using EduVibe.Modules.Notifications;
using EduVibe.Modules.Plans;
using EduVibe.Modules.Privacy;
using EduVibe.Modules.Reports;
using EduVibe.Modules.Students;
using EduVibe.Shared.Modules;

namespace EduVibe.Api;

/// <summary>Every module the API hosts. A new feature module is added here and nowhere else in the host.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new IdentityModule(),
        new CatalogModule(),
        new ClassesModule(),
        new StudentsModule(),
        new FeesModule(),
        new AttendanceModule(),
        new ReportsModule(),
        new ExamsModule(),
        new NotificationsModule(),
        new DashboardModule(),
        new PlansModule(),
        new PrivacyModule(),
    ];
}
