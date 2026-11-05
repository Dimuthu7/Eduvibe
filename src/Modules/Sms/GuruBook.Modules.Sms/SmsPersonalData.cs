using GuruBook.Shared.Privacy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Sms;

/// <summary>
/// A teacher's text log and channel choice are tenant-owned rows, so they are exported and erased with the rest of their data. A student's rows
/// hold their name, their parent's number and the words sent (marks, fees), so they go with the student: exported on request, deleted on erase,
/// together with any "stop texting me" recorded for a number nobody is messaged about any more.
/// </summary>
internal sealed class SmsPersonalData(SmsDbContext db) : TenantTablesSource<SmsDbContext>(db, SmsModule.ModuleName), IPersonalDataSource
{
    public async Task<PersonalData> ExportStudentAsync(Guid studentId, CancellationToken ct)
    {
        var rows = await Db.Messages.IgnoreQueryFilters().AsNoTracking().Where(m => m.StudentId == studentId).ToListAsync(ct);
        var type = Db.Model.FindEntityType(typeof(Domain.SmsMessage))!;
        return rows.Count == 0 ? PersonalData.Empty : new PersonalData { ["messages"] = [.. rows.Select(r => (object)ModuleData.Project(type, r))] };
    }

    public async Task EraseStudentAsync(Guid studentId, CancellationToken ct)
    {
        var phones = await Db.Messages.IgnoreQueryFilters().Where(m => m.StudentId == studentId).Select(m => m.ToPhone).Distinct().ToListAsync(ct);
        await Db.Messages.IgnoreQueryFilters().Where(m => m.StudentId == studentId).ExecuteDeleteAsync(ct);

        // Another student may share the parent's number; their texts keep the choice meaningful, so it stays while any remain.
        foreach (var phone in phones)
        {
            if (!await Db.Messages.IgnoreQueryFilters().AnyAsync(m => m.ToPhone == phone, ct))
            {
                await Db.OptOuts.Where(o => o.Phone == phone).ExecuteDeleteAsync(ct);
            }
        }
    }
}
