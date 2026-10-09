using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public const string ModuleName = "identity";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration, ModuleName);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // This module's endpoints are added in its sprint; see the sprint plan.
        endpoints.MapGroup($"/api/{ModuleName}").WithTags("Identity");
    }
}
