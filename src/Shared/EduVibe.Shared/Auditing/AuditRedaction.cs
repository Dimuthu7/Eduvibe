using Microsoft.EntityFrameworkCore;

namespace EduVibe.Shared.Auditing;

/// <summary>
/// Blanks what an audit row says about a person who has asked for their data to be erased. The row stays (who did what, and when),
/// so the log is still a complete trail, but the before and after values, which hold names and phone numbers, are gone.
/// </summary>
public static class AuditRedaction
{
    public const string Erased = """{"erased":true}""";

    /// <summary>Every row filed under a teacher: their classes, students, fees, attendance, marks and plan.</summary>
    public static Task<int> RedactTenantAsync(AuditDbContext db, Guid tenantId, CancellationToken ct) =>
        Blank(db.Entries.Where(e => e.TenantId == tenantId), ct);

    /// <summary>The rows about particular records, such as one student or one account.</summary>
    public static Task<int> RedactRecordsAsync(AuditDbContext db, string entity, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return Blank(db.Entries.Where(e => e.Entity == entity && list.Contains(e.EntityId)), ct);
    }

    // Two plain updates rather than one with a CASE: PostgreSQL will not mix text into a jsonb column unless the value is bound to the column.
    // Running it again rewrites the same marker, so a retry after a failure is harmless.
    private static async Task<int> Blank(IQueryable<AuditLogEntry> rows, CancellationToken ct)
    {
        var before = await rows.Where(e => e.Before != null).ExecuteUpdateAsync(s => s.SetProperty(e => e.Before, Erased), ct);
        var after = await rows.Where(e => e.After != null).ExecuteUpdateAsync(s => s.SetProperty(e => e.After, Erased), ct);
        return Math.Max(before, after);
    }
}
