using GuruBook.Modules.Materials.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Materials;

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
