using System.Globalization;
using GuruBook.Modules.Fees.Domain;

namespace GuruBook.Modules.Fees.Services;

/// <summary>Fee months are written "yyyy-MM", which sorts correctly as plain text.</summary>
internal static class FeeMonths
{
    public static string Of(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Today's date in Sri Lanka (UTC+5:30, no daylight saving).</summary>
    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    /// <summary>Today's month in Sri Lanka.</summary>
    public static string Current(TimeProvider clock) => Of(Today(clock));

    public static bool IsValid(string? month) =>
        month is { Length: 7 } && DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static string Add(string month, int months) =>
        Of(DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(months));

    /// <summary>The months from <paramref name="from"/> to <paramref name="to"/>, both included.</summary>
    public static IEnumerable<string> Range(string from, string to)
    {
        for (var month = from; string.CompareOrdinal(month, to) <= 0; month = Add(month, 1))
        {
            yield return month;
        }
    }

    /// <summary>
    /// The first month of the fee period that holds <paramref name="month"/>. Periods follow the calendar year: every 3 months starts
    /// in January, April, July and October; every 6 months in January and July; every 12 months in January.
    /// </summary>
    public static string PeriodStart(string month, int cycleMonths)
    {
        var number = int.Parse(month.AsSpan(5, 2), CultureInfo.InvariantCulture);
        var first = (number - 1) / cycleMonths * cycleMonths + 1;
        return $"{month[..5]}{first:00}";
    }

    /// <summary>The date a period's fee is due: the pay-by day of the period's first month (1 to 28, so it always exists).</summary>
    public static DateOnly DueDateOf(string periodStart, int payByDay) =>
        DateOnly.ParseExact(periodStart + "-" + Math.Clamp(payByDay, 1, 28).ToString("00", CultureInfo.InvariantCulture), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static MonthStatus StatusOf(decimal due, decimal paid) =>
        due <= 0 ? MonthStatus.FreeCard : paid >= due ? MonthStatus.Paid : paid > 0 ? MonthStatus.PartlyPaid : MonthStatus.Unpaid;
}
