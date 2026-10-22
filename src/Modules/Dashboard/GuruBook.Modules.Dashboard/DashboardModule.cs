using GuruBook.Modules.Dashboard.Endpoints;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Dashboard;

/// <summary>
/// The teacher's start screen. It stores only how the teacher arranged it; each widget gets its numbers from the endpoint of the
/// module that owns them, so this module reads no other module's data.
/// </summary>
public sealed class DashboardModule : IModule
{
    public const string ModuleName = "dashboard";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DashboardDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, DashboardPersonalData>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        LayoutEndpoints.Map(endpoints.MapGroup($"/api/{ModuleName}").WithTags("Dashboard"));
    }
}
