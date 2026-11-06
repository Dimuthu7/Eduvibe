using GuruBook.Modules.Payments.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Payments;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => PaymentsModule.ModuleName;

    public DbSet<BankDetails> BankDetails => Set<BankDetails>();
    public DbSet<PayQr> Qr => Set<PayQr>();
    public DbSet<PaymentClaim> Claims => Set<PaymentClaim>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}
