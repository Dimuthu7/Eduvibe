using EduVibe.Modules.Materials.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Materials;

internal sealed class DriveConnectionConfiguration : IEntityTypeConfiguration<DriveConnection>
{
    public void Configure(EntityTypeBuilder<DriveConnection> builder)
    {
        builder.ToTable("drive_connections");
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.RefreshToken).HasMaxLength(2000);
        builder.Property(c => c.RootFolderId).HasMaxLength(100);

        // One connection per teacher. Named, because the tenant index every module gets is not unique.
        builder.HasIndex(c => c.TenantId, "ux_drive_connections_tenant_id").IsUnique();
    }
}

internal sealed class ClassFolderConfiguration : IEntityTypeConfiguration<ClassFolder>
{
    public void Configure(EntityTypeBuilder<ClassFolder> builder)
    {
        builder.ToTable("class_folders");
        builder.Property(f => f.DriveFolderId).HasMaxLength(100);
        builder.HasIndex(f => f.ClassId, "ux_class_folders_class_id").IsUnique();
    }
}

internal sealed class MaterialFolderConfiguration : IEntityTypeConfiguration<MaterialFolder>
{
    public void Configure(EntityTypeBuilder<MaterialFolder> builder)
    {
        builder.ToTable("material_folders");
        builder.Property(f => f.Name).HasMaxLength(60);
        builder.Property(f => f.DriveFolderId).HasMaxLength(100);
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(f => new { f.ClassId, f.Kind });
    }
}

internal sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials");
        builder.Property(m => m.DriveFileId).HasMaxLength(100);
        builder.Property(m => m.DriveParentId).HasMaxLength(100);
        builder.Property(m => m.Title).HasMaxLength(120);
        builder.Property(m => m.FileName).HasMaxLength(200);
        builder.Property(m => m.ContentType).HasMaxLength(100);
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Topic).HasMaxLength(80);
        builder.HasIndex(m => m.ClassId);
        builder.HasIndex(m => m.FolderId);
    }
}
