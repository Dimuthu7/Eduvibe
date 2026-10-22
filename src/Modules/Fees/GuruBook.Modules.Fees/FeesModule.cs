using GuruBook.Shared.Privacy;
using GuruBook.Modules.Fees.Endpoints;
using GuruBook.Modules.Fees.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Fees;

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
        services.AddScoped<FeeReminderService>();
        services.AddScoped<IReportSource, FeeStatusReport>();
        services.AddHostedService<FeeDueJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Fees");
        FeeEndpoints.Map(group);
        WhatsAppEndpoints.Map(group);
        OverviewEndpoints.Map(group);
        MineEndpoints.Map(group);
    }
}
