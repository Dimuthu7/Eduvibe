using GuruBook.Modules.Messaging.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Messaging;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => MessagingModule.ModuleName;

    public DbSet<MessageSetting> Settings => Set<MessageSetting>();
    public DbSet<MessageText> Texts => Set<MessageText>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessagingDbContext).Assembly);
    }
}
