namespace GuruBook.Modules.WhatsApp.Client;

/// <summary>Accepts everything and sends nothing. For demos and the free test site, where there is no Meta account.</summary>
public sealed class SimulatedWhatsAppClient : IWhatsAppClient
{
    public bool IsConfigured => true;

    public Task<SendOutcome> SendTemplateAsync(string toDigits, string template, string language, IReadOnlyList<string> bodyParams, CancellationToken ct) =>
        Task.FromResult<SendOutcome>(new SendOutcome.Accepted("simulated-" + Guid.NewGuid().ToString("N")));
}
