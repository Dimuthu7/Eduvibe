using EduVibe.Shared.Privacy;
using EduVibe.Modules.Exams.Endpoints;
using EduVibe.Modules.Exams.Services;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Exams;

public sealed class ExamsModule : IModule
{
    public const string ModuleName = "exams";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ExamsDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, ExamsPersonalData>();
        services.AddScoped<GradeSchemeService>();
        services.AddScoped<ExamReader>();
        services.AddScoped<IReportSource, ExamResultsReport>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Exams");
        GradeSchemeEndpoints.Map(group);
        ProgressEndpoints.Map(group);
        ExamEndpoints.Map(group);
        HistoryEndpoints.Map(group);
    }
}
