using GuruBook.Modules.Fees.Domain;
using GuruBook.Modules.Fees.Endpoints;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Services;

/// <summary>A class's month, or the code of what was wrong with the request.</summary>
public sealed record ClassMonthResult(ClassMonthDto? Value, string? Error);

/// <summary>
/// One class for one month: every billed student's status and the class totals. Shared by the Fees screen and the Excel report
/// so both always show the same numbers.
/// </summary>
public sealed class ClassMonthService(FeesDbContext db, FeeDueService dues, IClassDirectory classes, IClassRoster roster, TimeProvider clock)
{
    /// <summary>How many months back a teacher can look.</summary>
    public const int MonthsBack = 36;

    /// <summary>Checks a month the teacher asked for (this month when none): not malformed, not older than 3 years, not in the future.</summary>
    public static string? CheckMonth(string? month, string current)
    {
        if (month is null)
        {
            return null;
        }

        if (!FeeMonths.IsValid(month) || string.CompareOrdinal(month, FeeMonths.Add(current, -MonthsBack)) < 0)
        {
            return "month_invalid";
        }

        return string.CompareOrdinal(month, current) > 0 ? "month_in_future" : null;
    }

    public async Task<ClassMonthResult> BuildAsync(Guid classId, string? month, CancellationToken ct)
    {
        var current = FeeMonths.Current(clock);
        if (CheckMonth(month, current) is { } problem)
        {
            return new ClassMonthResult(null, problem);
        }

        var chosen = month ?? current;
        if (await classes.FindAsync(classId, ct) is null)
        {
            return new ClassMonthResult(null, "class_not_found");
        }

        await dues.EnsureForClassAsync(classId, [chosen], ct);
        var rows = await db.Dues.Where(d => d.ClassId == classId && d.Month == chosen).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. rows.Select(r => r.Id)], ct);

        // Active students, plus anyone who has since left but was billed this month.
        var people = (await roster.ActiveRosterAsync(classId, ct)).ToDictionary(r => r.EnrolmentId);
        var gone = rows.Select(r => r.EnrolmentId).Where(e => !people.ContainsKey(e)).ToList();
        if (gone.Count > 0)
        {
            foreach (var (id, entry) in await roster.FindEnrolmentsAsync(gone, ct))
            {
                people[id] = entry;
            }
        }

        var lines = rows
            .Where(r => people.ContainsKey(r.EnrolmentId))
            .Select(r =>
            {
                var p = people[r.EnrolmentId];
                var got = paid.GetValueOrDefault(r.Id);
                return new MonthRowDto(r.EnrolmentId, p.IndexNumber, p.FullName, FeeMonths.StatusOf(r.AmountDue, got).ToString(), r.AmountDue, got);
            })
            .OrderBy(r => r.IndexNumber)
            .ToArray();
        return new ClassMonthResult(new ClassMonthDto(classId, chosen, Totals(lines.Select(l => (l.Status, l.Due, l.Paid))), lines), null);
    }

    /// <summary>Counts by status and the money. A free card owes nothing, so it is counted but adds no due.</summary>
    public static MonthTotalsDto Totals(IEnumerable<(string Status, decimal Due, decimal Paid)> rows)
    {
        var all = rows.ToList();
        var billed = all.Where(r => r.Status != nameof(MonthStatus.FreeCard)).ToList();
        var due = billed.Sum(r => r.Due);
        var collected = billed.Sum(r => r.Paid);
        return new MonthTotalsDto(
            all.Count(r => r.Status == nameof(MonthStatus.Paid)), all.Count(r => r.Status == nameof(MonthStatus.PartlyPaid)),
            all.Count(r => r.Status == nameof(MonthStatus.Unpaid)), all.Count(r => r.Status == nameof(MonthStatus.FreeCard)),
            due, collected, due - collected);
    }
}
