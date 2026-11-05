namespace GuruBook.Modules.Sms.Client;

/// <summary>Accepts everything and sends nothing. For demos and the free test site, where there is no account and no money to spend.</summary>
public sealed class SimulatedSmsClient : ISmsClient
{
    public bool IsConfigured => true;

    public Task<SmsOutcome> SendAsync(string toDigits, string text, CancellationToken ct) =>
        Task.FromResult<SmsOutcome>(new SmsOutcome.Accepted("simulated-" + Guid.NewGuid().ToString("N")));
}
