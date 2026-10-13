using EduVibe.Modules.Students.Endpoints;
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
        services.AddScoped<IClassRoster, ClassRoster>();
        services.AddScoped<IReportSource, ClassListReport>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Students");
        MeEndpoints.Map(group);
        StudentEndpoints.Map(group);
        InviteEndpoints.Map(group);
        RegistrationEndpoints.Map(group);
    }
}
