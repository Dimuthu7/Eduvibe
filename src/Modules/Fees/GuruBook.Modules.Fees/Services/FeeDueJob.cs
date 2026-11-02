using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Fees.Services;

/// <summary>
/// Makes this month's fee dues for every active enrolment of every teacher (and next month's, on the last day of the month, so the
/// "pay tomorrow" reminder has something to point at), shortly after the app starts and then every few hours. Every hour it also sends the
/// reminders that are due (see FeeReminderService, which stays quiet from 8 PM to 7 AM). It is safe to run any number of times. Turn off with Fees:MonthlyJob=false.
/// </summary>
internal sealed class FeeDueJob(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<FeeDueJob> log)
    : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MakeDuesEvery = TimeSpan.FromHours(6);

    private DateTimeOffset _duesMadeAt = DateTimeOffset.MinValue;
    private DateOnly _duesMadeOn;

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
            var now = clock.GetUtcNow();
            var today = FeeMonths.Today(clock);
            if (now - _duesMadeAt >= MakeDuesEvery || today != _duesMadeOn)
            {
                var months = new List<string> { FeeMonths.Current(clock) };
                var tomorrow = FeeMonths.Of(today.AddDays(1));
                if (tomorrow != months[0])
                {
                    months.Add(tomorrow);
                }

                var made = await scope.ServiceProvider.GetRequiredService<FeeDueService>().EnsureForAllTeachersAsync(months, ct);
                log.LogInformation("Monthly fee dues: {Count} made or updated", made);
                _duesMadeAt = now;
                _duesMadeOn = today;
            }

            var told = await scope.ServiceProvider.GetRequiredService<FeeReminderService>().RunAsync(ct);
            log.LogInformation("Fee reminders: {Count} offered", told);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Monthly fee dues failed; the next run will try again");
        }
    }
}
