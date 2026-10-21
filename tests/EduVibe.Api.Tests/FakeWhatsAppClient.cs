using EduVibe.Modules.WhatsApp.Client;

namespace EduVibe.Api.Tests;

/// <summary>A WhatsApp that records what it was asked to send. Tests can make it refuse or be busy.</summary>
public sealed class FakeWhatsAppClient : IWhatsAppClient
{
    private readonly Lock _gate = new();
    private int _next;

    public bool IsConfigured { get; set; } = true;

    /// <summary>When set, every send ends this way instead of being accepted.</summary>
    public SendOutcome? Outcome { get; set; }

    public List<(string To, string Template, string Language, string[] Params)> Sent { get; } = [];

    public Task<SendOutcome> SendTemplateAsync(string toDigits, string template, string language, IReadOnlyList<string> bodyParams, CancellationToken ct)
    {
        lock (_gate)
        {
            if (Outcome is { } outcome)
            {
                return Task.FromResult(outcome);
            }

            Sent.Add((toDigits, template, language, [.. bodyParams]));
            return Task.FromResult<SendOutcome>(new SendOutcome.Accepted("wamid." + ++_next));
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Sent.Clear();
            Outcome = null;
            IsConfigured = true;
        }
    }
}
