using GuruBook.Modules.Attendance.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Attendance;

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
