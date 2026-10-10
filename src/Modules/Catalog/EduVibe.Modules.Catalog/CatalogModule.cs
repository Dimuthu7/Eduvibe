using EduVibe.Modules.Catalog.Endpoints;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Catalog;

/// <summary>Lists that grow over time and are chosen from in dropdowns: teaching streams and subjects.</summary>
public sealed class CatalogModule : IModule
{
    public const string ModuleName = "catalog";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CatalogDbContext>(configuration, ModuleName);
        services.AddScoped<ICatalogDirectory, CatalogDirectory>();
        services.AddHostedService<CatalogSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        CatalogEndpoints.Map(endpoints.MapGroup($"/api/{ModuleName}").WithTags("Catalog"));
    }
}
