using EduVibe.Modules.Notifications.Endpoints;
using EduVibe.Modules.Notifications.Services;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Notifications;

/// <summary>
/// What teachers and students are told, in the app: a short list behind the profile menu with an unread count. Other modules add to it through
/// <see cref="INotifier"/> and know nothing of how it is stored.
/// </summary>
public sealed class NotificationsModule : IModule
{
    public const string ModuleName = "notifications";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotificationsDbContext>(configuration, ModuleName);
        services.AddScoped<INotifier, NotificationService>();
        services.AddScoped<IPersonalDataSource, NotificationsPersonalData>();
        services.AddHostedService<NotificationCleanupJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        NotificationEndpoints.Map(endpoints.MapGroup($"/api/{ModuleName}").WithTags("Notifications"));
    }
}
