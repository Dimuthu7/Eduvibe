using EduVibe.Modules.Classes.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Classes;

public sealed class ClassesDbContext(DbContextOptions<ClassesDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => ClassesModule.ModuleName;

    public DbSet<Institute> Institutes => Set<Institute>();
    public DbSet<InstituteTeacher> InstituteTeachers => Set<InstituteTeacher>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<TuitionClass> Classes => Set<TuitionClass>();
    public DbSet<ClassSlot> ClassSlots => Set<ClassSlot>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClassesDbContext).Assembly);
    }
}
