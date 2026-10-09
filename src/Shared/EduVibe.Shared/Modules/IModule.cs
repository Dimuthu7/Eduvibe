using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Shared.Modules;

/// <summary>
/// A feature area of EduVibe (Identity, Fees, Exams, ...). The API host discovers modules
/// through this interface, so adding a feature means adding a module, not editing the host.
/// </summary>
public interface IModule
{
    /// <summary>Short name, also used as the API route prefix: /api/{name}.</summary>
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
