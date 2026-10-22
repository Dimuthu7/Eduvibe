using GuruBook.Shared.Privacy;
using GuruBook.Modules.Attendance.Endpoints;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Attendance;

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
        WhatsAppEndpoints.Map(group);
        SummaryEndpoints.Map(group);
        WatchEndpoints.Map(group);
    }
}
