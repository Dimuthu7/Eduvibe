using GuruBook.Modules.Attendance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GuruBook.Modules.Attendance;

internal sealed class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        builder.ToTable("class_sessions");
        builder.Property(s => s.Note).HasMaxLength(200);
        builder.HasIndex(s => new { s.ClassId, s.Date, s.Start }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.Date });
    }
}

internal sealed class AttendanceMarkConfiguration : IEntityTypeConfiguration<AttendanceMark>
{
    public void Configure(EntityTypeBuilder<AttendanceMark> builder)
    {
        builder.ToTable("attendance_marks");
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<ClassSession>().WithMany().HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.SessionId, m.EnrolmentId }).IsUnique();
        builder.HasIndex(m => m.EnrolmentId);
    }
}
