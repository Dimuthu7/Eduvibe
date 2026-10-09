using EduVibe.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduVibe.Modules.Identity.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.Phone).HasMaxLength(20);
        builder.Property(u => u.FirstName).HasMaxLength(60);
        builder.Property(u => u.LastName).HasMaxLength(60);
        builder.Ignore(u => u.FullName);
        builder.Property(u => u.Email).HasMaxLength(200);
        builder.Property(u => u.Language).HasMaxLength(5);
        builder.Property(u => u.PasswordHash).HasMaxLength(300);
        builder.HasIndex(u => u.Phone).IsUnique();
        builder.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.Property(r => r.Role).HasMaxLength(30);
        builder.HasIndex(r => new { r.UserId, r.Role }).IsUnique();
    }
}

internal sealed class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> builder)
    {
        builder.ToTable("teachers");
        builder.Property(t => t.District).HasMaxLength(40);
        builder.HasMany(t => t.Subjects).WithOne().HasForeignKey(s => s.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.UserId).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeacherSubjectConfiguration : IEntityTypeConfiguration<TeacherSubject>
{
    public void Configure(EntityTypeBuilder<TeacherSubject> builder)
    {
        builder.ToTable("teacher_subjects");
        builder.HasIndex(s => new { s.TeacherId, s.SubjectId }).IsUnique();
        builder.HasIndex(s => s.SubjectId);
    }
}
