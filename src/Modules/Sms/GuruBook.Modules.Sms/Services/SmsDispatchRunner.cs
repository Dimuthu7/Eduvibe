using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Sms.Services;

/// <summary>
/// The one place that runs <see cref="SmsDispatcher"/>, so the background loop, a teacher pressing Send and a teacher opening the Messages
/// screen never send at the same time. A run that is asked for while one is going is skipped: the running one picks the new texts up.
/// Turned off by Sms:DispatchJob=false (the API tests run the dispatcher by hand).
/// </summary>
public sealed class SmsDispatchRunner(
    IServiceScopeFactory scopes, IConfiguration configuration, IHostApplicationLifetime lifetime, ILogger<SmsDispatchRunner> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Enabled => configuration.GetValue("Sms:DispatchJob", true);

    /// <summary>Starts a run in the background and returns at once. Safe to call often.</summary>
    public void Kick()
    {
        if (!Enabled || _gate.CurrentCount == 0)
        {
            return;
        }

        _ = Task.Run(() => RunAsync(lifetime.ApplicationStopping));
    }

    /// <summary>Sends everything that is due. Never throws, except when the app is stopping.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        if (!Enabled || !await _gate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<SmsDispatcher>();
            int handled;
            while ((handled = await dispatcher.RunOnceAsync(ct)) > 0)
            {
                log.LogInformation("SMS: handled {Count} queued text(s)", handled);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // A database or network hiccup (including a timeout, which .NET reports as a cancellation) must not stop sending for good.
            log.LogError(e, "Sending texts failed; the next run will try again");
        }
        finally
        {
            _gate.Release();
        }
    }
}
