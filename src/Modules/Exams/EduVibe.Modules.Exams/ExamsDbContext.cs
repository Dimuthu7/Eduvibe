using EduVibe.Modules.Exams.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Exams;

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
