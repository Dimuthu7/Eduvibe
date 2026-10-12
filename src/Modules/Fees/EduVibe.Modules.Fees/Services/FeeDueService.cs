using EduVibe.Modules.Fees.Domain;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Fees.Services;

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

    /// <summary>One of the current teacher's enrolments, for the given months (those before they joined are skipped).</summary>
    public async Task<int> EnsureForEnrolmentAsync(FeeBasis basis, IReadOnlyList<string> months, CancellationToken ct)
    {
        if (!basis.IsActive)
        {
            return 0;
        }

        return await EnsureAsync([basis], months, await classes.FindManyAsync([basis.ClassId], ct), ct);
    }

    private async Task<int> EnsureAsync(
        IReadOnlyList<FeeBasis> bases, IReadOnlyList<string> months, IReadOnlyDictionary<Guid, ClassSummary> classFees, CancellationToken ct)
    {
        if (bases.Count == 0 || months.Count == 0)
        {
            return 0;
        }

        // The ids come from the roster, which is already limited to the right teacher(s), so the tenant filter is lifted here.
        var ids = bases.Select(b => b.EnrolmentId).ToList();
        var existing = await db.Dues.IgnoreQueryFilters()
            .Where(d => ids.Contains(d.EnrolmentId) && months.Contains(d.Month))
            .ToListAsync(ct);
        var current = FeeMonths.Current(clock);

        // A due nobody has paid toward follows the enrolment's present plan and class fee until its month is over.
        var dueIds = existing.Where(d => string.CompareOrdinal(d.Month, current) >= 0).Select(d => d.Id).ToList();
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
            foreach (var month in months.Where(m => string.CompareOrdinal(m, joined) >= 0))
            {
                var due = existing.SingleOrDefault(d => d.EnrolmentId == basis.EnrolmentId && d.Month == month);
                if (due is null)
                {
                    db.Dues.Add(new FeeDue { TenantId = basis.TenantId, EnrolmentId = basis.EnrolmentId, ClassId = basis.ClassId, Month = month, AmountDue = amount });
                    changes++;
                }
                else if (string.CompareOrdinal(month, current) >= 0 && !touched.Contains(due.Id) && due.AmountDue != amount)
                {
                    due.AmountDue = amount;
                    changes++;
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
}
