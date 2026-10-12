using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EduVibe.Modules.Fees.Services;

/// <summary>
/// Makes this month's fee dues for every active enrolment of every teacher, shortly after the app starts and then every few
/// hours, so a new month's dues appear on their own. It is safe to run any number of times. Turn off with Fees:MonthlyJob=false.
/// </summary>
internal sealed class FeeDueJob(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<FeeDueJob> log)
    : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Fees:MonthlyJob", true))
        {
            return;
        }

        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var made = await scope.ServiceProvider.GetRequiredService<FeeDueService>()
                .EnsureForAllTeachersAsync([FeeMonths.Current(clock)], ct);
            log.LogInformation("Monthly fee dues: {Count} made or updated", made);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Monthly fee dues failed; the next run will try again");
        }
    }
}
