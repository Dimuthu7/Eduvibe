using EduVibe.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.WhatsApp;

/// <summary>
/// A teacher's message log and opt-outs are tenant-owned rows, so they are exported and erased with the rest of their data. A student's rows hold
/// their name, their parent's number and the words sent (marks, fees), so they go with the student: exported on request, deleted on erase,
/// together with any opt-out recorded for the numbers that were messaged about them.
/// </summary>
internal sealed class WhatsAppPersonalData(WhatsAppDbContext db) : TenantTablesSource<WhatsAppDbContext>(db, WhatsAppModule.ModuleName), IPersonalDataSource
{
    public async Task<PersonalData> ExportStudentAsync(Guid studentId, CancellationToken ct)
    {
        var rows = await Db.Messages.IgnoreQueryFilters().AsNoTracking().Where(m => m.StudentId == studentId).ToListAsync(ct);
        var type = Db.Model.FindEntityType(typeof(Domain.WhatsAppMessage))!;
        return rows.Count == 0 ? PersonalData.Empty : new PersonalData { ["messages"] = [.. rows.Select(r => (object)ModuleData.Project(type, r))] };
    }

    public async Task EraseStudentAsync(Guid studentId, CancellationToken ct)
    {
        var phones = await Db.Messages.IgnoreQueryFilters().Where(m => m.StudentId == studentId).Select(m => m.ToPhone).Distinct().ToListAsync(ct);
        await Db.Messages.IgnoreQueryFilters().Where(m => m.StudentId == studentId).ExecuteDeleteAsync(ct);

        // Another student may share the parent's number; their messages keep the opt-out meaningful, so it stays while any remain.
        foreach (var phone in phones)
        {
            if (!await Db.Messages.IgnoreQueryFilters().AnyAsync(m => m.ToPhone == phone, ct))
            {
                await Db.OptOuts.IgnoreQueryFilters().Where(o => o.Phone == phone).ExecuteDeleteAsync(ct);
            }
        }
    }
}
