using EduVibe.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Students;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");
        builder.Property(s => s.Phone).HasMaxLength(20);
        builder.Property(s => s.FirstName).HasMaxLength(60);
        builder.Property(s => s.LastName).HasMaxLength(60);
        builder.Property(s => s.School).HasMaxLength(120);
        builder.Property(s => s.Grade).HasMaxLength(30);
        builder.Property(s => s.GuardianName).HasMaxLength(120);
        builder.Property(s => s.GuardianPhone).HasMaxLength(20);
        builder.Ignore(s => s.FullName);
        builder.HasIndex(s => s.Phone).IsUnique();
        builder.HasIndex(s => s.UserId).IsUnique();
    }
}

internal sealed class EnrolmentConfiguration : IEntityTypeConfiguration<Enrolment>
{
    public void Configure(EntityTypeBuilder<Enrolment> builder)
    {
        builder.ToTable("enrolments");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.FeePlan).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.DiscountAmount).HasPrecision(12, 2);
        builder.HasOne(e => e.Student).WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ClassId, e.StudentId }).IsUnique();
        builder.HasIndex(e => new { e.ClassId, e.IndexNumber }).IsUnique();
    }
}

internal sealed class InviteLinkConfiguration : IEntityTypeConfiguration<InviteLink>
{
    public void Configure(EntityTypeBuilder<InviteLink> builder)
    {
        builder.ToTable("invite_links");
        builder.Property(i => i.Token).HasMaxLength(64);
        builder.HasIndex(i => i.Token).IsUnique();
        builder.HasIndex(i => i.ClassId);
    }
}
