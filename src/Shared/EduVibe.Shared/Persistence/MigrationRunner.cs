using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduVibe.Shared.Persistence;

/// <summary>Applies pending migrations for the audit context first, then every module context.</summary>
public static class MigrationRunner
{
    public static async Task MigrateAllAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MigrationRunner));

        foreach (var migratable in scope.ServiceProvider.GetServices<MigratableContext>())
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(migratable.ContextType);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                continue;
            }

            logger.LogInformation("Applying {Count} migration(s) for {Context}", pending.Count, migratable.ContextType.Name);
            await context.Database.MigrateAsync(cancellationToken);
        }
    }
}
