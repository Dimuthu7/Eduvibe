using GuruBook.Shared.Privacy;

namespace GuruBook.Modules.Support;

/// <summary>
/// A teacher's tickets and the conversation on them are tenant-owned rows, so they go with the rest of the teacher's data: exported on
/// request, erased with the teacher. The support number belongs to nobody and stays.
/// </summary>
internal sealed class SupportPersonalData(SupportDbContext db)
    : TenantTablesSource<SupportDbContext>(db, SupportModule.ModuleName), IPersonalDataSource;
