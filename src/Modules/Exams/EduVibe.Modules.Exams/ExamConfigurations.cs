using EduVibe.Modules.Exams.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Exams;

internal sealed class GradeSchemeConfiguration : IEntityTypeConfiguration<GradeScheme>
{
    public void Configure(EntityTypeBuilder<GradeScheme> builder)
    {
        builder.ToTable("grade_schemes");
        builder.Property(s => s.Name).HasMaxLength(100);
        // Every teacher's scheme is made with the same name, so this stops two requests at once from making two.
        builder.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
    }
}

internal sealed class GradeBandConfiguration : IEntityTypeConfiguration<GradeBand>
{
    public void Configure(EntityTypeBuilder<GradeBand> builder)
    {
        builder.ToTable("grade_bands");
        builder.Property(b => b.Grade).HasMaxLength(5);
        builder.Property(b => b.MinPercent).HasPrecision(5, 2);
        builder.HasOne<GradeScheme>().WithMany().HasForeignKey(b => b.GradeSchemeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => b.GradeSchemeId);
    }
}

internal sealed class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> builder)
    {
        builder.ToTable("exams");
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.HasOne<GradeScheme>().WithMany().HasForeignKey(e => e.GradeSchemeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ClassId, e.HeldOn });
    }
}

internal sealed class ExamPartConfiguration : IEntityTypeConfiguration<ExamPart>
{
    public void Configure(EntityTypeBuilder<ExamPart> builder)
    {
        builder.ToTable("exam_parts");
        builder.Property(p => p.Name).HasMaxLength(50);
        builder.Property(p => p.MaxMark).HasPrecision(7, 2);
        builder.HasOne<Exam>().WithMany().HasForeignKey(p => p.ExamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => p.ExamId);
    }
}

internal sealed class ExamResultConfiguration : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> builder)
    {
        builder.ToTable("exam_results");
        builder.Property(r => r.Total).HasPrecision(9, 2);
        builder.Property(r => r.Percent).HasPrecision(5, 2);
        builder.HasOne<Exam>().WithMany().HasForeignKey(r => r.ExamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.ExamId, r.EnrolmentId }).IsUnique();
        builder.HasIndex(r => r.EnrolmentId);
    }
}

internal sealed class ExamPartMarkConfiguration : IEntityTypeConfiguration<ExamPartMark>
{
    public void Configure(EntityTypeBuilder<ExamPartMark> builder)
    {
        builder.ToTable("exam_part_marks");
        builder.Property(m => m.Mark).HasPrecision(7, 2);
        builder.HasOne<ExamResult>().WithMany().HasForeignKey(m => m.ExamResultId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExamPart>().WithMany().HasForeignKey(m => m.ExamPartId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.ExamResultId, m.ExamPartId }).IsUnique();
        builder.HasIndex(m => m.ExamPartId);
    }
}
