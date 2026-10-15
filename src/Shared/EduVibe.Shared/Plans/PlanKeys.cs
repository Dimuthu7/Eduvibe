namespace EduVibe.Shared.Plans;

/// <summary>
/// The things a plan can switch on, from a fixed list in code. The Super Admin chooses which a plan includes;
/// a new paid feature adds a key here and gates itself with <see cref="PlanGateExtensions.RequireFeature"/>.
/// </summary>
public static class PlanFeatures
{
    public const string WhatsApp = "whatsapp";
    public const string DriveMaterials = "drive_materials";
    public const string ParentPortal = "parent_portal";
    public const string AdvancedAnalytics = "advanced_analytics";

    public static readonly IReadOnlyList<string> All = [WhatsApp, DriveMaterials, ParentPortal, AdvancedAnalytics];
}

/// <summary>The numbers a plan can cap. A plan with no value for a limit sets no cap.</summary>
public static class PlanLimits
{
    public const string MaxClasses = "max_classes";
    public const string MaxStudentsPerClass = "max_students_per_class";
    public const string WhatsAppPerMonth = "whatsapp_per_month";

    public static readonly IReadOnlyList<string> All = [MaxClasses, MaxStudentsPerClass, WhatsAppPerMonth];
}
