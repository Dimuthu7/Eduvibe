using EduVibe.Modules.Catalog.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Catalog;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => CatalogModule.ModuleName;

    public DbSet<EducationStream> Streams => Set<EducationStream>();
    public DbSet<Subject> Subjects => Set<Subject>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
