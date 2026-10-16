using EduVibe.Shared.Privacy;
using EduVibe.Modules.Attendance.Endpoints;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Attendance;

public sealed class AttendanceModule : IModule
{
    public const string ModuleName = "attendance";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AttendanceDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, AttendancePersonalData>();
        services.AddScoped<IAttendanceLookup, AttendanceLookup>();
        services.AddScoped<IReportSource, AttendanceRegisterReport>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Attendance");
        SessionEndpoints.Map(group);
        SummaryEndpoints.Map(group);
        WatchEndpoints.Map(group);
    }
}
