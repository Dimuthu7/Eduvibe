using GuruBook.Shared.Privacy;
using GuruBook.Modules.Students.Endpoints;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Students;

public sealed class StudentsModule : IModule
{
    public const string ModuleName = "students";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<StudentsDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, StudentsPersonalData>();
        services.AddScoped<IClassRoster, ClassRoster>();
        services.AddScoped<IStudentCards, StudentCards>();
        services.AddScoped<IStudentContacts, StudentContacts>();
        services.AddScoped<IReportSource, ClassListReport>();
        services.AddScoped<IStatsSource, StudentsStats>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Students");
        MeEndpoints.Map(group);
        PhotoEndpoints.Map(group);
        StudentEndpoints.Map(group);
        ParentEndpoints.Map(group);
        CardEndpoints.Map(group);
        InviteEndpoints.Map(group);
        RegistrationEndpoints.Map(group);
    }
}
