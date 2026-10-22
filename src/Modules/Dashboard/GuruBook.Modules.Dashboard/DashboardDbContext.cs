using GuruBook.Modules.Dashboard.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Dashboard;

public sealed class DashboardDbContext(DbContextOptions<DashboardDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => DashboardModule.ModuleName;

    public DbSet<DashboardLayout> Layouts => Set<DashboardLayout>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DashboardDbContext).Assembly);
    }
}
