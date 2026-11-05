using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Payments.Services;

/// <summary>
/// Runs <see cref="SlipCleaner"/> shortly after the app starts and then twice a day. Turn off with Payments:SlipCleanupJob=false.
/// </summary>
internal sealed class SlipCleanupJob(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SlipCleanupJob> log)
    : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Payments:SlipCleanupJob", true))
        {
            return;
        }

        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<SlipCleaner>().RunOnceAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Clearing old bank slips failed; the next run will try again");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
    }
}
