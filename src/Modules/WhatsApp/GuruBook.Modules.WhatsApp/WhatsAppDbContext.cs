using GuruBook.Modules.WhatsApp.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.WhatsApp;

public sealed class WhatsAppDbContext(DbContextOptions<WhatsAppDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => WhatsAppModule.ModuleName;

    public DbSet<WhatsAppMessage> Messages => Set<WhatsAppMessage>();
    public DbSet<WhatsAppOptOut> OptOuts => Set<WhatsAppOptOut>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WhatsAppDbContext).Assembly);
    }
}
