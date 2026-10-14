namespace EduVibe.Modules.Exams.Services;

/// <summary>A grade and the lowest percentage that earns it.</summary>
public sealed record BandRule(string Grade, decimal MinPercent);

/// <summary>The exam arithmetic in one place: percentages, grades and competition ranks.</summary>
public static class ExamCalculator
{
    public static readonly IReadOnlyList<BandRule> OlDefault =
    [
        new("A", 75m), new("B", 65m), new("C", 50m), new("S", 35m), new("W", 0m),
    ];

    /// <summary>Marks to two decimal places, halves rounding up.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>The total as a percentage of the most it could be, to two places. Null when the exam has no marks to score.</summary>
    public static decimal? Percent(decimal total, decimal maxTotal) =>
        maxTotal <= 0 ? null : Round(100m * total / maxTotal);

    /// <summary>The grade of the highest band the percentage reaches, or null when it reaches none (a scheme should always have a band at 0).</summary>
    public static string? GradeFor(decimal percent, IEnumerable<BandRule> bands) =>
        bands.OrderByDescending(b => b.MinPercent).FirstOrDefault(b => percent >= b.MinPercent)?.Grade;

    /// <summary>
    /// Competition ranking by total, highest first: equal totals share a rank and the next rank is skipped (1, 2, 2, 4).
    /// </summary>
    public static Dictionary<Guid, int> Rank(IEnumerable<(Guid Id, decimal Total)> entries)
    {
        var list = entries.ToList();
        return list.ToDictionary(e => e.Id, e => 1 + list.Count(other => other.Total > e.Total));
    }
}
