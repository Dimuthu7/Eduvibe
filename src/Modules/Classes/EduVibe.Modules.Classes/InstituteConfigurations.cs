using EduVibe.Modules.Classes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Classes;

internal sealed class InstituteConfiguration : IEntityTypeConfiguration<Institute>
{
    public void Configure(EntityTypeBuilder<Institute> builder)
    {
        builder.ToTable("institutes");
        builder.Property(i => i.Name).HasMaxLength(120);
        builder.Property(i => i.District).HasMaxLength(40);
        builder.Property(i => i.Town).HasMaxLength(80);
        builder.Property(i => i.Address).HasMaxLength(250);
        builder.Property(i => i.Phone).HasMaxLength(20);
    }
}

internal sealed class InstituteTeacherConfiguration : IEntityTypeConfiguration<InstituteTeacher>
{
    public void Configure(EntityTypeBuilder<InstituteTeacher> builder)
    {
        builder.ToTable("institute_teachers");
        builder.HasIndex(l => new { l.InstituteId, l.TeacherId }).IsUnique();
        builder.HasIndex(l => l.TeacherId);
        builder.HasOne<Institute>().WithMany().HasForeignKey(l => l.InstituteId).OnDelete(DeleteBehavior.Cascade);
    }
}
