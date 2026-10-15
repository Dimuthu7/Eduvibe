using EduVibe.Shared.Privacy;
using EduVibe.Modules.Fees.Endpoints;
using EduVibe.Modules.Fees.Services;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Fees;

public sealed class FeesModule : IModule
{
    public const string ModuleName = "fees";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<FeesDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, FeesPersonalData>();
        services.AddScoped<FeeDueService>();
        services.AddScoped<ClassMonthService>();
        services.AddScoped<IReportSource, FeeStatusReport>();
        services.AddHostedService<FeeDueJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Fees");
        FeeEndpoints.Map(group);
        OverviewEndpoints.Map(group);
        MineEndpoints.Map(group);
    }
}
