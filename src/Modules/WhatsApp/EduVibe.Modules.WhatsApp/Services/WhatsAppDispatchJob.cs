using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EduVibe.Modules.WhatsApp.Services;

/// <summary>
/// Runs the dispatcher when teachers queue messages and every half minute otherwise (to pick up retries). Turn off with WhatsApp:DispatchJob=false.
/// </summary>
internal sealed class WhatsAppDispatchJob(IServiceScopeFactory scopes, WhatsAppSignal signal, IConfiguration configuration, ILogger<WhatsAppDispatchJob> log)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("WhatsApp:DispatchJob", true))
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await signal.WaitAsync(Interval, stoppingToken);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    while (await scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>().RunOnceAsync(stoppingToken) > 0)
                    {
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Sending WhatsApp messages failed; the next run will try again");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
    }
}
