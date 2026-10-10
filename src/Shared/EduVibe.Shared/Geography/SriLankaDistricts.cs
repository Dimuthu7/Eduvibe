namespace EduVibe.Shared.Geography;

/// <summary>The 25 districts of Sri Lanka. Stored by name; the web app reads the list from the API.</summary>
public static class SriLankaDistricts
{
    public static readonly IReadOnlyList<string> All =
    [
        "Ampara", "Anuradhapura", "Badulla", "Batticaloa", "Colombo", "Galle", "Gampaha", "Hambantota", "Jaffna",
        "Kalutara", "Kandy", "Kegalle", "Kilinochchi", "Kurunegala", "Mannar", "Matale", "Matara", "Monaragala",
        "Mullaitivu", "Nuwara Eliya", "Polonnaruwa", "Puttalam", "Ratnapura", "Trincomalee", "Vavuniya",
    ];

    /// <summary>The district's official spelling for a name in any letter case, or null when it is not a district.</summary>
    public static string? Canonical(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : All.FirstOrDefault(d => string.Equals(d, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
