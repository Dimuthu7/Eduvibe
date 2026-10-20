using EduVibe.Modules.Materials.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Materials;

public sealed class MaterialsDbContext(DbContextOptions<MaterialsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => MaterialsModule.ModuleName;

    public DbSet<DriveConnection> Connections => Set<DriveConnection>();
    public DbSet<ClassFolder> ClassFolders => Set<ClassFolder>();
    public DbSet<MaterialFolder> Folders => Set<MaterialFolder>();
    public DbSet<Material> Materials => Set<Material>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MaterialsDbContext).Assembly);
    }
}
