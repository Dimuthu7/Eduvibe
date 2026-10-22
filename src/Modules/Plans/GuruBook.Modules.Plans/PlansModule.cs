using GuruBook.Shared.Privacy;
using GuruBook.Modules.Plans.Endpoints;
using GuruBook.Modules.Plans.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Plans;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Plans;

/// <summary>
/// Decides what a teacher can use. The Super Admin makes plans from a fixed list of features and limits and puts teachers on them;
/// other modules ask <see cref="IPlanGate"/> before a gated action.
/// </summary>
public sealed class PlansModule : IModule
{
    public const string ModuleName = "plans";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PlansDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, PlansPersonalData>();
        services.AddScoped<PlanResolver>();
        services.AddScoped<IPlanGate, PlanGate>();
        services.AddScoped<IStatsSource, PlansStats>();
        services.AddHostedService<PlanSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Plans");
        PlanEndpoints.Map(group);
        TeacherPlanEndpoints.Map(group);
        MyPlanEndpoints.Map(group);
    }
}
