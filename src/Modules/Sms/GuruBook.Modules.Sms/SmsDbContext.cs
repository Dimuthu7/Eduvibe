using GuruBook.Modules.Sms.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Sms;

public sealed class SmsDbContext(DbContextOptions<SmsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => SmsModule.ModuleName;

    public DbSet<SmsMessage> Messages => Set<SmsMessage>();
    public DbSet<SmsOptOut> OptOuts => Set<SmsOptOut>();
    public DbSet<SmsSetting> Settings => Set<SmsSetting>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SmsDbContext).Assembly);
    }
}
