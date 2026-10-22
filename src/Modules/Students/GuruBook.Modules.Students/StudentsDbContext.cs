using GuruBook.Modules.Students.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students;

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
