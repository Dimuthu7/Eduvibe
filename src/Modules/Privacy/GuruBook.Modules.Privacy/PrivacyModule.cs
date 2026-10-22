using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Privacy;

/// <summary>
/// Hands over or erases what the app holds about a teacher or a student, when the Super Admin acts on a request. It holds no data of its own:
/// each module that does offers an <see cref="GuruBook.Shared.Privacy.IPersonalDataSource"/>, and a new module takes part by registering one.
/// </summary>
public sealed class PrivacyModule : IModule
{
    public const string ModuleName = "privacy";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Privacy").RequireAuthorization(Policies.SuperAdmin);
        PrivacyEndpoints.Map(group);
    }
}
