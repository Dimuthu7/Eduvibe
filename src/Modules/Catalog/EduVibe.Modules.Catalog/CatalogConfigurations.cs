using EduVibe.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Catalog;

internal sealed class EducationStreamConfiguration : IEntityTypeConfiguration<EducationStream>
{
    public void Configure(EntityTypeBuilder<EducationStream> builder)
    {
        builder.ToTable("education_streams");
        builder.Property(s => s.Name).HasMaxLength(80);
        builder.HasIndex(s => s.Name).IsUnique();
    }
}

internal sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");
        builder.Property(s => s.Name).HasMaxLength(80);
        builder.HasIndex(s => s.Name).IsUnique();
    }
}
