using GuruBook.Modules.Identity.Endpoints;
using GuruBook.Modules.Identity.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Privacy;
using GuruBook.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public const string ModuleName = "identity";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration, ModuleName);
        services.AddSingleton<PasswordService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TeacherProfileRules>();
        services.AddScoped<ITeacherDirectory, TeacherDirectory>();
        services.AddScoped<IAccountProvisioner, AccountProvisioner>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IdentityPersonalData>();
        services.AddScoped<IPersonalDataSource>(sp => sp.GetRequiredService<IdentityPersonalData>());
        services.AddScoped<IStatsSource, IdentityStats>();
        services.AddHostedService<SuperAdminSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Identity");
        IdentityEndpoints.Map(group);
        TeacherAdminEndpoints.Map(group);
    }
}
