using System.Globalization;
using GuruBook.Modules.Fees.Domain;

namespace GuruBook.Modules.Fees.Services;

/// <summary>Fee months are written "yyyy-MM", which sorts correctly as plain text.</summary>
internal static class FeeMonths
{
    public static string Of(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Today's month in Sri Lanka (UTC+5:30, no daylight saving).</summary>
    public static string Current(TimeProvider clock) =>
        Of(DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime));

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

    public static MonthStatus StatusOf(decimal due, decimal paid) =>
        due <= 0 ? MonthStatus.FreeCard : paid >= due ? MonthStatus.Paid : paid > 0 ? MonthStatus.PartlyPaid : MonthStatus.Unpaid;
}
