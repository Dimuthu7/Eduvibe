using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.WhatsApp.Services;

/// <summary>
/// Sends queued messages as soon as a teacher queues them and every half minute otherwise (to pick up retries and anything left from before a
/// restart). Only the app stopping ends the loop; any other error is logged and the next round tries again. Turn off with WhatsApp:DispatchJob=false.
/// </summary>
internal sealed class WhatsAppDispatchJob(
    WhatsAppDispatchRunner runner, WhatsAppSignal signal, IOptions<WhatsAppOptions> options, ILogger<WhatsAppDispatchJob> log)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!runner.Enabled)
        {
            log.LogInformation("WhatsApp sender is turned off (WhatsApp:DispatchJob=false)");
            return;
        }

        log.LogInformation("WhatsApp sender started in {Mode} mode", options.Value.Mode);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Messages left queued from before a restart go out on the first round, without waiting.
                await runner.RunAsync(stoppingToken);
                await signal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogError(e, "WhatsApp sender round failed; trying again shortly");
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
