namespace GuruBook.Modules.WhatsApp.Services;

/// <summary>Wakes the background sender as soon as a teacher queues messages, so they go out within seconds instead of at the next check.</summary>
public sealed class WhatsAppSignal
{
    private readonly SemaphoreSlim _wake = new(0);

    public void Wake() => _wake.Release();

    /// <summary>Waits until woken or until <paramref name="timeout"/> passes, then forgets any extra wake-ups.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        await _wake.WaitAsync(timeout, ct);
        while (_wake.CurrentCount > 0 && _wake.Wait(0))
        {
        }
    }
}
