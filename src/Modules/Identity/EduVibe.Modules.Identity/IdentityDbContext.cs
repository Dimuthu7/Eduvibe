using EduVibe.Modules.Identity.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Identity;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => IdentityModule.ModuleName;

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
