namespace GuruBook.Shared.Modules;

/// <summary>
/// What a person can switch off. A parent mutes one topic for one child; the words are stored with the notification so both ends agree.
/// </summary>
public static class AlertTopics
{
    /// <summary>A payment recorded, or one taken back.</summary>
    public const string Payments = "payments";

    /// <summary>Arrived or absent, once a session is saved.</summary>
    public const string Attendance = "attendance";

    /// <summary>Results published, and a mark corrected afterwards.</summary>
    public const string Results = "results";

    public static readonly IReadOnlyList<string> All = [Payments, Attendance, Results];

    public static bool IsKnown(string? topic) => topic is not null && All.Contains(topic);
}

/// <summary>How much of attendance a teacher wants parents told about.</summary>
public static class AttendanceAlerts
{
    /// <summary>Every mark: a parent hears that their child arrived as well as when they did not.</summary>
    public const string All = "all";

    /// <summary>Only absences.</summary>
    public const string Absences = "absences";

    /// <summary>Nothing.</summary>
    public const string Off = "off";

    public static readonly IReadOnlyList<string> Choices = [All, Absences, Off];
}

/// <summary>What the current teacher's parents are told. The Notifications module implements it; Attendance uses it.</summary>
public interface IParentAlertSettings
{
    /// <summary>One of <see cref="AttendanceAlerts"/>, for the signed-in teacher.</summary>
    Task<string> AttendanceAlertsAsync(CancellationToken cancellationToken = default);
}
