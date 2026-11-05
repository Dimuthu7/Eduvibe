using GuruBook.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Payments;

/// <summary>
/// A teacher's bank details, their QR and their claims are tenant-owned rows, so they go with the teacher. A student's claims say what they
/// paid and carry a photo of their bank slip, so those go with the student: exported on request and deleted on erase, image and all.
/// </summary>
internal sealed class PaymentsPersonalData(PaymentsDbContext db)
    : TenantTablesSource<PaymentsDbContext>(db, PaymentsModule.ModuleName), IPersonalDataSource
{
    public async Task<PersonalData> ExportStudentAsync(Guid studentId, CancellationToken ct)
    {
        var rows = await Db.Claims.IgnoreQueryFilters().AsNoTracking().Where(c => c.StudentId == studentId).ToListAsync(ct);
        var type = Db.Model.FindEntityType(typeof(Domain.PaymentClaim))!;
        return rows.Count == 0 ? PersonalData.Empty : new PersonalData { ["claims"] = [.. rows.Select(r => (object)ModuleData.Project(type, r))] };
    }

    public Task EraseStudentAsync(Guid studentId, CancellationToken ct) =>
        Db.Claims.IgnoreQueryFilters().Where(c => c.StudentId == studentId).ExecuteDeleteAsync(ct);
}
