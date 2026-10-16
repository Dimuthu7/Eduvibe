using EduVibe.Modules.Dashboard.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Dashboard;

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
