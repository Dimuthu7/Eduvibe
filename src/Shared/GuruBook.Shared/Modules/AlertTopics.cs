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
