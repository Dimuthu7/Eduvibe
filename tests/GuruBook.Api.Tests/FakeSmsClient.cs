using GuruBook.Modules.Sms.Client;

namespace GuruBook.Api.Tests;

/// <summary>An SMS provider that records what it was asked to send. Tests can make it refuse or be busy.</summary>
public sealed class FakeSmsClient : ISmsClient
{
    private readonly Lock _gate = new();
    private int _next;

    public bool IsConfigured { get; set; } = true;

    /// <summary>When set, every send ends this way instead of being accepted.</summary>
    public SmsOutcome? Outcome { get; set; }

    public List<(string To, string Text)> Sent { get; } = [];

    public Task<SmsOutcome> SendAsync(string toDigits, string text, CancellationToken ct)
    {
        lock (_gate)
        {
            if (Outcome is { } outcome)
            {
                return Task.FromResult(outcome);
            }

            Sent.Add((toDigits, text));
            return Task.FromResult<SmsOutcome>(new SmsOutcome.Accepted("textlk-" + ++_next));
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
