using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students;

public sealed class StudentsDbContext(DbContextOptions<StudentsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => StudentsModule.ModuleName;

    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentPhoto> StudentPhotos => Set<StudentPhoto>();
    public DbSet<Enrolment> Enrolments => Set<Enrolment>();
    public DbSet<InviteLink> InviteLinks => Set<InviteLink>();
    public DbSet<ParentLink> ParentLinks => Set<ParentLink>();
    public DbSet<StudentCard> StudentCards => Set<StudentCard>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudentsDbContext).Assembly);
    }
}
