using GuruBook.Modules.Fees.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees;

public sealed class FeesDbContext(DbContextOptions<FeesDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => FeesModule.ModuleName;

    public DbSet<FeeDue> Dues => Set<FeeDue>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> Allocations => Set<PaymentAllocation>();
    public DbSet<ClassReminderSetting> ReminderSettings => Set<ClassReminderSetting>();
    public DbSet<ReminderSkip> ReminderSkips => Set<ReminderSkip>();
    public DbSet<FeeReminder> Reminders => Set<FeeReminder>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FeesDbContext).Assembly);
    }
}
