using GuruBook.Modules.Sms.Client;
using GuruBook.Modules.Sms.Endpoints;
using GuruBook.Modules.Sms.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.Sms;

/// <summary>
/// Text messages to parents (a Pro feature), for the parents who are not on WhatsApp. The same drafts the WhatsApp module takes are turned into
/// short words here, queued and sent through an SMS provider in the background. A teacher chooses whether their paid messages go by WhatsApp
/// first or by SMS alone; every text is paid for by the part, so the allowance counts parts and a refused text is never retried.
/// </summary>
public sealed class SmsModule : IModule
{
    public const string ModuleName = "sms";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SmsDbContext>(configuration, ModuleName);
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.Section));
        services.AddHttpClient<TextLkSmsClient>();
        services.AddSingleton<SimulatedSmsClient>();
        services.AddSingleton<NoSmsClient>();
        services.AddScoped<ISmsClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SmsOptions>>().Value;
            return options.IsSimulated ? sp.GetRequiredService<SimulatedSmsClient>()
                : options.IsTextLk ? sp.GetRequiredService<TextLkSmsClient>()
                : sp.GetRequiredService<NoSmsClient>();
        });
        services.AddSingleton<SmsSignal>();
        services.AddSingleton<SmsDispatchRunner>();
        services.AddScoped<ISmsSender, SmsSender>();
        services.AddScoped<SmsDispatcher>();
        services.AddScoped<IPersonalDataSource, SmsPersonalData>();
        services.AddHostedService<SmsDispatchJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/api/{ModuleName}").WithTags("SMS");
        SmsEndpoints.Map(group);
    }
}
