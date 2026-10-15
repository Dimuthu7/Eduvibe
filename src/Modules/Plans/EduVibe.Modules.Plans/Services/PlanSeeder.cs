using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EduVibe.Modules.Plans.Services;

/// <summary>Adds the Free and Pro plans to an empty database. Turn off with Plans:SeedOnStartup=false.</summary>
internal sealed class PlanSeeder(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Plans:SeedOnStartup", true))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        await PlanDefaults.EnsureAsync(scope.ServiceProvider.GetRequiredService<PlansDbContext>(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
