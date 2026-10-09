using EduVibe.Modules.Identity.Endpoints;
using EduVibe.Modules.Identity.Services;
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
        services.AddSingleton<PasswordService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ITeacherDirectory, TeacherDirectory>();
        services.AddHostedService<SuperAdminSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Identity");
        IdentityEndpoints.Map(group);
        TeacherAdminEndpoints.Map(group);
    }
}
