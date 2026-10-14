namespace EduVibe.Modules.Exams.Services;

/// <summary>A grade and the lowest percentage that earns it.</summary>
public sealed record BandRule(string Grade, decimal MinPercent);

/// <summary>How many students earned one grade.</summary>
public sealed record GradeCount(string Grade, int Count);

/// <summary>
/// How a class did in one exam, from the students who sat it and have every part marked. Marks are totals out of the
/// exam's maximum. PassMark is the percentage needed to pass (null when the scheme has a single grade, so everyone passes).
/// </summary>
public sealed record ExamStats(
    int Sat, decimal? Highest, decimal? Lowest, decimal? Average, decimal? Median, decimal? PassMark, int Passed, int? PassRate,
    IReadOnlyList<GradeCount> Grades);

/// <summary>The exam arithmetic in one place: percentages, grades, competition ranks and the class analysis.</summary>
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

    /// <summary>The middle value, or the average of the two middle values when there is an even number. Null for none.</summary>
    public static decimal? Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0)
        {
            return null;
        }

        var middle = sorted.Count / 2;
        return Round(sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2);
    }

    /// <summary>
    /// The lowest percentage that passes: where the second-lowest grade starts, so only the lowest grade (W in the O/L
    /// scheme) fails. Null when the scheme has one grade.
    /// </summary>
    public static decimal? PassMark(IEnumerable<BandRule> bands) =>
        bands.Select(b => b.MinPercent).Distinct().Order().Skip(1).Cast<decimal?>().FirstOrDefault();

    /// <summary>Highest, lowest, average and median total, pass rate and how many earned each grade (best grade first).</summary>
    public static ExamStats Stats(IReadOnlyCollection<(decimal Total, decimal Percent)> scored, IReadOnlyCollection<BandRule> bands)
    {
        var passMark = PassMark(bands);
        var passed = scored.Count(s => passMark is not { } mark || s.Percent >= mark);
        var grades = scored.Select(s => GradeFor(s.Percent, bands)).ToList();
        var counts = bands.OrderByDescending(b => b.MinPercent).Select(b => new GradeCount(b.Grade, grades.Count(g => g == b.Grade))).ToList();
        if (scored.Count == 0)
        {
            return new ExamStats(0, null, null, null, null, passMark, 0, null, counts);
        }

        return new ExamStats(
            scored.Count, scored.Max(s => s.Total), scored.Min(s => s.Total), Round(scored.Average(s => s.Total)), Median(scored.Select(s => s.Total)),
            passMark, passed, (int)Math.Round(100m * passed / scored.Count, MidpointRounding.AwayFromZero), counts);
    }
}
