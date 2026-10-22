using GuruBook.Shared.Privacy;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using GuruBook.Modules.Classes.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Classes;

public sealed class ClassesModule : IModule
{
    public const string ModuleName = "classes";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ClassesDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, ClassesPersonalData>();
        services.AddScoped<IClassDirectory, ClassDirectory>();
        services.AddScoped<IStatsSource, ClassesStats>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Classes");
        InstituteEndpoints.Map(group);
        VenueEndpoints.Map(group);
        ClassEndpoints.Map(group);
    }
}
