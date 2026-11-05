namespace GuruBook.Modules.Sms.Client;

/// <summary>What happened when one text was handed to the SMS provider.</summary>
public abstract record SmsOutcome
{
    /// <summary>The provider accepted it. Delivery, where the provider reports it, comes later.</summary>
    public sealed record Accepted(string ProviderMessageId) : SmsOutcome;

    /// <summary>Worth trying again later: the provider was busy or unreachable.</summary>
    public sealed record Retry(string Reason) : SmsOutcome;

    /// <summary>Will never work as asked (a bad number, no credit, the sender name not approved); Code is the short code shown to the teacher.</summary>
    public sealed record Rejected(string Code) : SmsOutcome;
}

/// <summary>
/// The only place that talks to the SMS provider. Everything else works with messages in the log, so tests use a fake and the provider's
/// details stay here. text.lk is the first provider; another one is a second class behind this interface.
/// </summary>
public interface ISmsClient
{
    /// <summary>False when nothing is set up on this server; then nothing is queued.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends one text. <paramref name="toDigits"/> is the number without the plus sign.</summary>
    Task<SmsOutcome> SendAsync(string toDigits, string text, CancellationToken ct);
}
