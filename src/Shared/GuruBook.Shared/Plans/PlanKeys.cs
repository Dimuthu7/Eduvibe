namespace GuruBook.Shared.Plans;

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
    public const string Sms = "sms";

    public static readonly IReadOnlyList<string> All = [WhatsApp, DriveMaterials, ParentPortal, AdvancedAnalytics, Sms];
}

/// <summary>The numbers a plan can cap. A plan with no value for a limit sets no cap.</summary>
public static class PlanLimits
{
    public const string MaxClasses = "max_classes";
    public const string MaxStudentsPerClass = "max_students_per_class";
    public const string WhatsAppPerMonth = "whatsapp_per_month";

    /// <summary>Message parts, not messages: Sinhala and Tamil fit about 70 letters in a part against 160 for English, and each part is paid for.</summary>
    public const string SmsPerMonth = "sms_per_month";

    public static readonly IReadOnlyList<string> All = [MaxClasses, MaxStudentsPerClass, WhatsAppPerMonth, SmsPerMonth];
}
