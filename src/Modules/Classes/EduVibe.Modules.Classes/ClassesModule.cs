using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EduVibe.Modules.Classes.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Classes;

public sealed class ClassesModule : IModule
{
    public const string ModuleName = "classes";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ClassesDbContext>(configuration, ModuleName);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        InstituteEndpoints.Map(endpoints.MapGroup($"/api/{ModuleName}").WithTags("Classes"));
    }
}
