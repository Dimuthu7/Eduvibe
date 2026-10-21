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

internal sealed class StudentPhotoConfiguration : IEntityTypeConfiguration<StudentPhoto>
{
    public void Configure(EntityTypeBuilder<StudentPhoto> builder)
    {
        builder.ToTable("student_photos");
        builder.Property(p => p.ContentType).HasMaxLength(20);
        builder.HasOne<Student>().WithMany().HasForeignKey(p => p.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => p.StudentId).IsUnique();
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

internal sealed class ParentLinkConfiguration : IEntityTypeConfiguration<ParentLink>
{
    public void Configure(EntityTypeBuilder<ParentLink> builder)
    {
        builder.ToTable("parent_links");
        builder.HasOne<Student>().WithMany().HasForeignKey(l => l.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => new { l.TenantId, l.StudentId, l.ParentUserId }).IsUnique();
        builder.HasIndex(l => l.ParentUserId);
    }
}

internal sealed class StudentCardConfiguration : IEntityTypeConfiguration<StudentCard>
{
    public void Configure(EntityTypeBuilder<StudentCard> builder)
    {
        builder.ToTable("student_cards");
        builder.Property(c => c.Code).HasMaxLength(32);
        builder.HasOne<Student>().WithMany().HasForeignKey(c => c.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.TenantId, c.StudentId }).IsUnique();
        builder.HasIndex(c => c.Code).IsUnique();
    }
}
