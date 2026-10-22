namespace GuruBook.Modules.WhatsApp.Client;

/// <summary>What happened when one message was handed to WhatsApp.</summary>
public abstract record SendOutcome
{
    /// <summary>WhatsApp accepted it. Delivery is reported later by the webhook.</summary>
    public sealed record Accepted(string ProviderMessageId) : SendOutcome;

    /// <summary>Worth trying again later: WhatsApp was busy or unreachable.</summary>
    public sealed record Retry(string Reason) : SendOutcome;

    /// <summary>Will never work as asked (number not on WhatsApp, template missing); Code is the short code shown to the teacher.</summary>
    public sealed record Rejected(string Code) : SendOutcome;
}

/// <summary>
/// The only place that talks to WhatsApp. Everything else works with messages in the log, so tests use a fake and the Meta details stay here.
/// </summary>
public interface IWhatsAppClient
{
    /// <summary>False when nothing is set up on this server; then nothing is queued.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends an approved template with its body values in order. <paramref name="toDigits"/> is the number without the plus sign.</summary>
    Task<SendOutcome> SendTemplateAsync(string toDigits, string template, string language, IReadOnlyList<string> bodyParams, CancellationToken ct);
}
