using GuruBook.Modules.Fees.Domain;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Services;

/// <summary>
/// Makes the monthly fee dues. A due is class fee minus the enrolment's discount, or nothing for a free card, and exists
/// from the month the student joined. Making them again changes nothing, so it is safe for the nightly job, for a teacher
/// opening a month and for a payment to ask at any time.
/// </summary>
public sealed class FeeDueService(FeesDbContext db, IClassRoster roster, IClassDirectory classes, TimeProvider clock)
{
    public static decimal AmountFor(FeeBasis basis, decimal classFee) => basis.FeePlan switch
    {
        "FreeCard" => 0m,
        "Discount" => Math.Max(0m, classFee - basis.DiscountAmount),
        _ => classFee,
    };

    /// <summary>Every teacher's active enrolments, for the given months. Called by the nightly job, which acts for no teacher.</summary>
    public async Task<int> EnsureForAllTeachersAsync(IReadOnlyList<string> months, CancellationToken ct)
    {
        var bases = await roster.FeeBasisForAllTeachersAsync(ct);
        return await EnsureAsync(bases, months, await classes.FindManyInAnyTenantAsync(bases.Select(b => b.ClassId), ct), ct);
    }

    /// <summary>The current teacher's active students in one class.</summary>
    public async Task<int> EnsureForClassAsync(Guid classId, IReadOnlyList<string> months, CancellationToken ct)
    {
        var bases = await roster.FeeBasisAsync(classId, ct);
        return await EnsureAsync(bases, months, await classes.FindManyAsync([classId], ct), ct);
    }

    /// <summary>All of the current teacher's active students, for the given months.</summary>
    public async Task<int> EnsureForCurrentTeacherAsync(IReadOnlyList<string> months, CancellationToken ct)
    {
        var bases = await roster.FeeBasisAsync(null, ct);
        return await EnsureAsync(bases, months, await classes.FindManyAsync(bases.Select(b => b.ClassId).Distinct(), ct), ct);
    }

    /// <summary>One of the current teacher's enrolments, for the given months (those before they joined are skipped).</summary>
    public async Task<int> EnsureForEnrolmentAsync(FeeBasis basis, IReadOnlyList<string> months, CancellationToken ct)
    {
        if (!basis.IsActive)
        {
            return 0;
        }

        return await EnsureAsync([basis], months, await classes.FindManyAsync([basis.ClassId], ct), ct);
    }

    /// <summary>A student who joins after a period's pay-by day still gets this many days to pay their first fee.</summary>
    private const int JoiningGraceDays = 7;

    private async Task<int> EnsureAsync(
        IReadOnlyList<FeeBasis> bases, IReadOnlyList<string> months, IReadOnlyDictionary<Guid, ClassSummary> classFees, CancellationToken ct)
    {
        if (bases.Count == 0 || months.Count == 0)
        {
            return 0;
        }

        // The ids come from the roster, which is already limited to the right teacher(s), so the tenant filter is lifted here.
        // The window reaches a year either side so a due that merely covers a requested month is found, and so are its neighbours.
        var ids = bases.Select(b => b.EnrolmentId).ToList();
        var ordered = months.Order(StringComparer.Ordinal).ToList();
        var windowStart = FeeMonths.Add(ordered[0], -12);
        var windowEnd = FeeMonths.Add(ordered[^1], 12);
        var existing = await db.Dues.IgnoreQueryFilters()
            .Where(d => ids.Contains(d.EnrolmentId) && d.Month.CompareTo(windowStart) >= 0 && d.Month.CompareTo(windowEnd) <= 0)
            .ToListAsync(ct);
        var current = FeeMonths.Current(clock);

        // A due nobody has paid toward follows the enrolment's present plan and class fee until its last month is over.
        var dueIds = existing.Where(d => string.CompareOrdinal(d.LastMonth, current) >= 0).Select(d => d.Id).ToList();
        var touched = dueIds.Count == 0
            ? []
            : (await db.Allocations.IgnoreQueryFilters()
                .Where(a => dueIds.Contains(a.FeeDueId))
                .Join(db.Payments.IgnoreQueryFilters().Where(p => p.VoidedAt == null), a => a.PaymentId, p => p.Id, (a, _) => a.FeeDueId)
                .ToListAsync(ct)).ToHashSet();

        var changes = 0;
        foreach (var basis in bases)
        {
            if (!classFees.TryGetValue(basis.ClassId, out var cls) || cls.IsArchived)
            {
                continue;
            }

            var amount = AmountFor(basis, cls.MonthlyFee);
            var joined = FeeMonths.Of(basis.JoinedOn);
            var mine = existing.Where(d => d.EnrolmentId == basis.EnrolmentId).ToList();

            foreach (var month in ordered.Where(m => string.CompareOrdinal(m, joined) >= 0))
            {
                var due = mine.FirstOrDefault(d => d.Covers(month));
                if (due is null)
                {
                    due = NewDue(basis, cls, month, joined, amount, mine);
                    mine.Add(due);
                    existing.Add(due);
                    db.Dues.Add(due);
                    changes++;
                }
                else if (string.CompareOrdinal(due.LastMonth, current) >= 0)
                {
                    var dueDate = DueDateFor(due.Month, cls.PayByDay, joined, basis.JoinedOn);
                    if (due.DueDate != dueDate)
                    {
                        due.DueDate = dueDate;
                        changes++;
                    }

                    if (!touched.Contains(due.Id) && due.AmountDue != amount)
                    {
                        due.AmountDue = amount;
                        changes++;
                    }
                }
            }
        }

        if (changes == 0)
        {
            return 0;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A request or the job made the same dues a moment ago and the unique index stopped the duplicates.
            db.ChangeTracker.Clear();
            return 0;
        }

        return changes;
    }

    /// <summary>
    /// A due for the fee period holding <paramref name="month"/>. It starts at the period's first month, but never before the student
    /// joined or before the end of the enrolment's previous due, and it ends before the next due begins, so changing a class's cycle
    /// never overlaps what was already billed.
    /// </summary>
    private static FeeDue NewDue(FeeBasis basis, ClassSummary cls, string month, string joined, decimal amount, List<FeeDue> mine)
    {
        var cycle = Math.Max(1, cls.FeeCycleMonths);
        var periodStart = FeeMonths.PeriodStart(month, cycle);
        var start = periodStart;
        var end = FeeMonths.Add(periodStart, cycle - 1);

        if (string.CompareOrdinal(joined, start) > 0)
        {
            start = joined;
        }

        var before = mine.Where(d => string.CompareOrdinal(d.LastMonth, month) < 0).Select(d => d.LastMonth).DefaultIfEmpty().Max();
        if (before is { Length: > 0 } && string.CompareOrdinal(FeeMonths.Add(before, 1), start) > 0)
        {
            start = FeeMonths.Add(before, 1);
        }

        var after = mine.Where(d => string.CompareOrdinal(d.Month, month) > 0).Select(d => d.Month).DefaultIfEmpty().Min();
        if (after is { Length: > 0 } && string.CompareOrdinal(FeeMonths.Add(after, -1), end) < 0)
        {
            end = FeeMonths.Add(after, -1);
        }

        var periodMonths = MonthsBetween(start, end);
        return new FeeDue
        {
            TenantId = basis.TenantId, EnrolmentId = basis.EnrolmentId, ClassId = basis.ClassId, Month = start, LastMonth = end,
            PeriodMonths = periodMonths, AmountDue = amount, DueDate = DueDateFor(start, cls.PayByDay, joined, basis.JoinedOn),
        };
    }

    private static int MonthsBetween(string from, string to) =>
        (int.Parse(to.AsSpan(0, 4)) - int.Parse(from.AsSpan(0, 4))) * 12 + int.Parse(to.AsSpan(5, 2)) - int.Parse(from.AsSpan(5, 2)) + 1;

    /// <summary>The pay-by day of the due's first month, or a week after joining when the student joined in that month, whichever is later.</summary>
    private static DateOnly DueDateFor(string firstMonth, int payByDay, string joinedMonth, DateOnly joinedOn)
    {
        var date = FeeMonths.DueDateOf(firstMonth, payByDay);
        var grace = joinedOn.AddDays(JoiningGraceDays);
        return firstMonth == joinedMonth && grace > date ? grace : date;
    }
}
