using GuruBook.Shared.Privacy;

namespace GuruBook.Modules.Dashboard;

/// <summary>A teacher's layout is a tenant-owned row, so it is exported and erased with the rest of their data.</summary>
internal sealed class DashboardPersonalData(DashboardDbContext db) : TenantTablesSource<DashboardDbContext>(db, DashboardModule.ModuleName);
