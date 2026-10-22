using GuruBook.Modules.Fees.Domain;
using GuruBook.Shared.Auditing;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees;

/// <summary>
/// Fees rows for a teacher's export and erasure, and for a student's export through their enrolments. Erasing a student keeps the
/// amounts but clears the reasons given for voiding their payments, which are free text a teacher may have written a name into.
/// </summary>
internal sealed class FeesPersonalData(FeesDbContext db, IClassRoster roster, AuditDbContext audit)
    : EnrolmentTablesSource<FeesDbContext>(db, FeesModule.ModuleName, roster), IPersonalDataSource
{
    private readonly IClassRoster _roster = roster;

    public async Task EraseStudentAsync(Guid studentId, CancellationToken ct)
    {
        var enrolments = (await _roster.EnrolmentsOfStudentAsync(studentId, ct)).Select(e => e.EnrolmentId).ToList();
        var payments = await Db.Payments.IgnoreQueryFilters().Where(p => enrolments.Contains(p.EnrolmentId)).Select(p => p.Id).ToListAsync(ct);
        if (payments.Count == 0)
        {
            return;
        }

        await Db.Payments.IgnoreQueryFilters().Where(p => payments.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.VoidReason, (string?)null), ct);
        await AuditRedaction.RedactRecordsAsync(audit, nameof(Payment), payments, ct);
    }
}
