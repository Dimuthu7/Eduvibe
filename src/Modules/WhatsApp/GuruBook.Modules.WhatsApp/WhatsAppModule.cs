using GuruBook.Modules.WhatsApp.Client;
using GuruBook.Modules.WhatsApp.Endpoints;
using GuruBook.Modules.WhatsApp.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.WhatsApp;

/// <summary>
/// WhatsApp messages to parents and students (a Pro feature): fee reminders, absence alerts and results. Fees, Attendance and Exams decide who
/// should be told and hand drafts to <see cref="IWhatsAppSender"/>; this module checks the plan and the monthly allowance, queues the messages
/// and sends them through the WhatsApp Cloud API in the background. It is the second module that calls an outside service.
/// </summary>
public sealed class WhatsAppModule : IModule
{
    public const string ModuleName = "whatsapp";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<WhatsAppDbContext>(configuration, ModuleName);
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.Section));
        services.AddHttpClient<MetaWhatsAppClient>();
        services.AddSingleton<SimulatedWhatsAppClient>();
        services.AddSingleton<NoWhatsAppClient>();
        services.AddScoped<IWhatsAppClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
            return options.IsSimulated ? sp.GetRequiredService<SimulatedWhatsAppClient>()
                : options.IsMeta ? sp.GetRequiredService<MetaWhatsAppClient>()
                : sp.GetRequiredService<NoWhatsAppClient>();
        });
        services.AddSingleton<WhatsAppSignal>();
        services.AddScoped<IWhatsAppSender, WhatsAppSender>();
        services.AddScoped<WhatsAppDispatcher>();
        services.AddScoped<WhatsAppWebhook>();
        services.AddScoped<IPersonalDataSource, WhatsAppPersonalData>();
        services.AddHostedService<WhatsAppDispatchJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("WhatsApp");
        WhatsAppEndpoints.Map(group);
        WebhookEndpoints.Map(group);
    }
}
