using EduVibe.Modules.Attendance;
using EduVibe.Modules.Catalog;
using EduVibe.Modules.Classes;
using EduVibe.Modules.Exams;
using EduVibe.Modules.Fees;
using EduVibe.Modules.Identity;
using EduVibe.Modules.Plans;
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
        new ExamsModule(),
        new PlansModule(),
    ];
}
