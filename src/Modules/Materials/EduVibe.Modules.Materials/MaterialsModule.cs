using EduVibe.Modules.Materials.Drive;
using EduVibe.Modules.Materials.Endpoints;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Materials;

/// <summary>
/// Class notes and past papers. The files live in the teacher's own Google Drive (drive.file scope: only what GuruBook created);
/// this module stores where each one is and how to list it, and hands the bytes to students through the API so Drive is never shared.
/// It is the first module that calls an outside service.
/// </summary>
public sealed class MaterialsModule : IModule
{
    public const string ModuleName = "materials";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<MaterialsDbContext>(configuration, ModuleName);
        services.Configure<DriveOptions>(configuration.GetSection(DriveOptions.Section));
        services.AddSingleton<DriveTokens>();
        // A file can be 25 MB over a phone connection, so the default 100 seconds is too short.
        services.AddHttpClient<IDriveClient, GoogleDriveClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddScoped<IPersonalDataSource, MaterialsPersonalData>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}");
        DriveEndpoints.Map(group);
        MaterialEndpoints.Map(group);
        StudentMaterialEndpoints.Map(group);
    }
}
