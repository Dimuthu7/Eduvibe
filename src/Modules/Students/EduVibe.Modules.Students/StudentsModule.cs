using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Students;

public sealed class StudentsModule : IModule
{
    public const string ModuleName = "students";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<StudentsDbContext>(configuration, ModuleName);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // This module's endpoints are added in its sprint; see the sprint plan.
        endpoints.MapGroup($"/api/{ModuleName}").WithTags("Students");
    }
}
