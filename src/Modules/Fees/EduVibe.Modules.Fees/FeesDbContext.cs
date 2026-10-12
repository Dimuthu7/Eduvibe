using EduVibe.Modules.Fees.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Fees;

public sealed class FeesDbContext(DbContextOptions<FeesDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => FeesModule.ModuleName;

    public DbSet<FeeDue> Dues => Set<FeeDue>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> Allocations => Set<PaymentAllocation>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FeesDbContext).Assembly);
    }
}
