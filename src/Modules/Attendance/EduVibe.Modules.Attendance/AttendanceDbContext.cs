using EduVibe.Modules.Attendance.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Attendance;

public sealed class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => AttendanceModule.ModuleName;

    public DbSet<ClassSession> Sessions => Set<ClassSession>();
    public DbSet<AttendanceMark> Marks => Set<AttendanceMark>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AttendanceDbContext).Assembly);
    }
}
