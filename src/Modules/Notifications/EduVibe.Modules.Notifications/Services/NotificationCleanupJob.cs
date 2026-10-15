using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EduVibe.Modules.Notifications.Services;

/// <summary>
/// Clears out old notifications once a day so the table stays small: read ones after 90 days, and anything after 180 days even if it was
/// never opened. Turn off with Notifications:CleanupJob=false.
/// </summary>
public sealed class NotificationCleanupJob(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<NotificationCleanupJob> log)
    : BackgroundService
{
    public static readonly TimeSpan ReadKeptFor = TimeSpan.FromDays(90);
    public static readonly TimeSpan AnyKeptFor = TimeSpan.FromDays(180);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:CleanupJob", true))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
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
            var removed = await DeleteOldAsync(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>(), clock.GetUtcNow(), ct);
            log.LogInformation("Notification clean-up removed {Count}", removed);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Notification clean-up failed; the next run will try again");
        }
    }

    public static async Task<int> DeleteOldAsync(NotificationsDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        // The dates are compared here, not in the query: the tests run on SQLite, which cannot compare them. The table never holds more than
        // half a year of notifications, so reading three small columns is cheap.
        var readBefore = now - ReadKeptFor;
        var anyBefore = now - AnyKeptFor;
        var rows = await db.Notifications.Select(n => new { n.Id, n.ReadAt, n.UpdatedAt }).ToListAsync(ct);
        var old = rows.Where(n => (n.ReadAt is { } read && read < readBefore) || n.UpdatedAt < anyBefore).Select(n => n.Id).ToList();
        return old.Count == 0 ? 0 : await db.Notifications.Where(n => old.Contains(n.Id)).ExecuteDeleteAsync(ct);
    }
}
