using EduVibe.Modules.Fees.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Fees.Services;

internal static class FeeLedger
{
    /// <summary>
    /// What has been paid toward each due, counting only payments that were not voided. Summed here because SQLite, used in
    /// the tests, cannot add up decimals. The due ids come from a query that was already limited to the right teacher.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> PaidByDueAsync(FeesDbContext db, IReadOnlyCollection<Guid> dueIds, CancellationToken ct)
    {
        if (dueIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Allocations.IgnoreQueryFilters()
            .Where(a => dueIds.Contains(a.FeeDueId))
            .Join(db.Payments.IgnoreQueryFilters().Where(p => p.VoidedAt == null), a => a.PaymentId, p => p.Id, (a, _) => new { a.FeeDueId, a.Amount })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.FeeDueId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
    }
}
