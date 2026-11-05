namespace GuruBook.Modules.Sms.Client;

/// <summary>Stands in while SMS is not set up, so the rest of the module needs no special case.</summary>
public sealed class NoSmsClient : ISmsClient
{
    public bool IsConfigured => false;

    public Task<SmsOutcome> SendAsync(string toDigits, string text, CancellationToken ct) =>
        Task.FromResult<SmsOutcome>(new SmsOutcome.Rejected("not_configured"));
}
