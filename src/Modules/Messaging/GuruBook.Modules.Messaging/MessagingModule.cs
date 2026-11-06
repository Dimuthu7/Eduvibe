using GuruBook.Modules.Messaging.Endpoints;
using GuruBook.Modules.Messaging.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Modules.Messaging;

/// <summary>
/// Which messages GuruBook sends on its own, how they are sent, and what they say (a Pro feature). Everything is on and in the app to start
/// with; a teacher can switch an event off, put it on WhatsApp or a text message, rewrite the words of the paid ones in each language, and add
/// their own line to the WhatsApp ones. The modules that know who should be told ask <see cref="IMessageRouter"/> to send and know nothing
/// about channels.
/// </summary>
public sealed class MessagingModule : IModule
{
    public const string ModuleName = "messaging";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<MessagingDbContext>(configuration, ModuleName);
        services.AddScoped<IMessageSettings, MessageSettings>();
        services.AddScoped<IMessageRouter, MessageRouter>();
        services.AddScoped<IPersonalDataSource, MessagingPersonalData>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MessagingEndpoints.Map(endpoints.MapGroup($"/api/{ModuleName}").WithTags("Messaging"));
    }
}
