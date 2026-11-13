using GuruBook.Modules.Support.Endpoints;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Support;

/// <summary>
/// Report an issue, for a teacher on a plan with support (a Pro feature). They see the number to call, write what went wrong, and read what
/// support writes back. The Super Admin works through the same tickets from the other side and sets the number everyone is shown.
/// </summary>
public sealed class SupportModule : IModule
{
    public const string ModuleName = "support";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SupportDbContext>(configuration, ModuleName);
        services.AddScoped<IPersonalDataSource, SupportPersonalData>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Support");
        TicketEndpoints.Map(group);
        AdminEndpoints.Map(group);
    }
}
