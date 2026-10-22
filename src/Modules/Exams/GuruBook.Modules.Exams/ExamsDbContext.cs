using GuruBook.Modules.Exams.Domain;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Exams;

public sealed class ExamsDbContext(DbContextOptions<ExamsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => ExamsModule.ModuleName;

    public DbSet<GradeScheme> GradeSchemes => Set<GradeScheme>();
    public DbSet<GradeBand> GradeBands => Set<GradeBand>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamPart> Parts => Set<ExamPart>();
    public DbSet<ExamResult> Results => Set<ExamResult>();
    public DbSet<ExamPartMark> PartMarks => Set<ExamPartMark>();

    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExamsDbContext).Assembly);
    }
}
