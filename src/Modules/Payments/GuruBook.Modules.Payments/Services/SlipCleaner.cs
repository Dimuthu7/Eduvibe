using GuruBook.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Payments.Services;

/// <summary>
/// Clears bank slip images a few months after the teacher decided on them. The claim itself stays, so there is still a record of what was
/// said and what receipt it became; only the photo of the payer's bank slip goes, because nothing needs it once the money is accounted for.
/// </summary>
public sealed class SlipCleaner(PaymentsDbContext db, TimeProvider clock, ILogger<SlipCleaner> log)
{
    /// <summary>How long a slip is kept after the decision. Long enough for a dispute, short enough not to hoard bank documents.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(120);

    /// <summary>Clears the slips that are past the keep period, and says how many. Safe to run any number of times.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - KeepFor;

        // The cleanup acts for no teacher, so the tenant filter is lifted. Dates are compared here rather than in the query, because not
        // every database can compare an offset; the rows it looks through are only the decided claims that still hold an image.
        var decided = await db.Claims.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Status != ClaimStatus.Open && c.DecidedAt != null && c.SlipRemovedAt == null)
            .Select(c => new { c.Id, c.DecidedAt })
            .ToListAsync(ct);
        var old = decided.Where(c => c.DecidedAt < cutoff).Select(c => c.Id).ToList();
        if (old.Count == 0)
        {
            return 0;
        }

        var cleared = await db.Claims.IgnoreQueryFilters()
            .Where(c => old.Contains(c.Id))
            .ExecuteUpdateAsync(c => c
                .SetProperty(x => x.Slip, (byte[]?)null)
                .SetProperty(x => x.SlipContentType, (string?)null)
                .SetProperty(x => x.SlipRemovedAt, now), ct);
        if (cleared > 0)
        {
            log.LogInformation("Bank slips cleared: {Count}", cleared);
        }

        return cleared;
    }
}
