using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.Sms.Services;

/// <summary>
/// Sends queued texts as soon as a teacher queues them and every half minute otherwise (to pick up retries and anything left from before a
/// restart). Only the app stopping ends the loop; any other error is logged and the next round tries again. Turn off with Sms:DispatchJob=false.
/// </summary>
internal sealed class SmsDispatchJob(
    SmsDispatchRunner runner, SmsSignal signal, IOptions<SmsOptions> options, ILogger<SmsDispatchJob> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!runner.Enabled)
        {
            log.LogInformation("SMS sender is turned off (Sms:DispatchJob=false)");
            return;
        }

        log.LogInformation("SMS sender started in {Mode} mode", options.Value.Mode);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Texts left queued from before a restart go out on the first round, without waiting.
                await runner.RunAsync(stoppingToken);
                await signal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogError(e, "SMS sender round failed; trying again shortly");
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
