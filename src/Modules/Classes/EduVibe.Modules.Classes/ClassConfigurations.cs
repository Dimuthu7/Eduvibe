using EduVibe.Modules.Classes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Classes;

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");
        builder.Property(v => v.Name).HasMaxLength(120);
        builder.Property(v => v.District).HasMaxLength(40);
        builder.Property(v => v.Town).HasMaxLength(80);
        builder.Property(v => v.Address).HasMaxLength(250);
    }
}

internal sealed class TuitionClassConfiguration : IEntityTypeConfiguration<TuitionClass>
{
    public void Configure(EntityTypeBuilder<TuitionClass> builder)
    {
        builder.ToTable("classes");
        builder.Property(c => c.Title).HasMaxLength(120);
        builder.Property(c => c.Medium).HasMaxLength(20);
        builder.Property(c => c.MonthlyFee).HasPrecision(12, 2);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasMany(c => c.Slots).WithOne().HasForeignKey(s => s.ClassId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClassSlotConfiguration : IEntityTypeConfiguration<ClassSlot>
{
    public void Configure(EntityTypeBuilder<ClassSlot> builder)
    {
        builder.ToTable("class_slots");
        builder.HasIndex(s => s.ClassId);
    }
}
