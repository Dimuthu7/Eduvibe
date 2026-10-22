namespace GuruBook.Modules.WhatsApp.Client;

/// <summary>Stands in while WhatsApp is not set up, so the rest of the module needs no special case.</summary>
public sealed class NoWhatsAppClient : IWhatsAppClient
{
    public bool IsConfigured => false;

    public Task<SendOutcome> SendTemplateAsync(string toDigits, string template, string language, IReadOnlyList<string> bodyParams, CancellationToken ct) =>
        Task.FromResult<SendOutcome>(new SendOutcome.Rejected("not_configured"));
}
