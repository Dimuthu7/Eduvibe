using GuruBook.Modules.Fees.Domain;
using GuruBook.Modules.Fees.Endpoints;

namespace GuruBook.Modules.Fees.Services;

/// <summary>Where a fee stands against its due date: late, due today, due within a few days, or nothing to say.</summary>
internal static class FeeTiming
{
    /// <summary>How many days ahead a fee counts as "due soon".</summary>
    public const int SoonDays = 3;

    public const string Overdue = "Overdue";
    public const string DueToday = "DueToday";
    public const string DueSoon = "DueSoon";

    /// <summary>
    /// Empty when nothing is owed (paid, free card) or the due date is more than <see cref="SoonDays"/> days away.
    /// DaysLate is how many days past the due date it is, or 0.
    /// </summary>
    public static (string Timing, int DaysLate) Of(MonthStatus status, DateOnly dueDate, DateOnly today)
    {
        if (status is MonthStatus.Paid or MonthStatus.FreeCard)
        {
            return ("", 0);
        }

        var days = dueDate.DayNumber - today.DayNumber;
        return days < 0 ? (Overdue, -days) : days == 0 ? (DueToday, 0) : days <= SoonDays ? (DueSoon, 0) : ("", 0);
    }

    public static DueDto ToDto(FeeDue due, decimal paid, DateOnly today)
    {
        var status = FeeMonths.StatusOf(due.AmountDue, paid);
        var (timing, late) = Of(status, due.DueDate, today);
        return new DueDto(due.Month, due.AmountDue, paid, status.ToString(), due.DueDate, due.PeriodMonths, timing, late, due.LastMonth);
    }
}
