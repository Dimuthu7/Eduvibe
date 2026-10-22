using GuruBook.Modules.Attendance;
using GuruBook.Modules.Catalog;
using GuruBook.Modules.Classes;
using GuruBook.Modules.Dashboard;
using GuruBook.Modules.Exams;
using GuruBook.Modules.Fees;
using GuruBook.Modules.Identity;
using GuruBook.Modules.Materials;
using GuruBook.Modules.Notifications;
using GuruBook.Modules.Plans;
using GuruBook.Modules.Privacy;
using GuruBook.Modules.Reports;
using GuruBook.Modules.Students;
using GuruBook.Modules.WhatsApp;
using GuruBook.Shared.Modules;

namespace GuruBook.Api;

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
        new MaterialsModule(),
        new WhatsAppModule(),
        new PrivacyModule(),
    ];
}
