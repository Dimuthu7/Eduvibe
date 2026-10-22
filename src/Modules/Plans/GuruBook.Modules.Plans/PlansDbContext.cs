using GuruBook.Modules.Plans.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Plans;

public sealed class PlansDbContext(DbContextOptions<PlansDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => PlansModule.ModuleName;

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanFeature> Features => Set<PlanFeature>();
    public DbSet<TeacherSubscription> Subscriptions => Set<TeacherSubscription>();
    public DbSet<PlanPayment> Payments => Set<PlanPayment>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlansDbContext).Assembly);
    }
}
