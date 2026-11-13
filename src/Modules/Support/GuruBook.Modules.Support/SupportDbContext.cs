using GuruBook.Modules.Support.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Support;

public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => SupportModule.ModuleName;

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketReply> Replies => Set<TicketReply>();
    public DbSet<SupportSettings> Settings => Set<SupportSettings>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportDbContext).Assembly);
    }
}
