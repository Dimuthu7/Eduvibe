using GuruBook.Modules.Notifications.Push;

namespace GuruBook.Api.Tests;

/// <summary>A push service that records what it was asked to send, so no test reaches Google, Mozilla or Apple. Tests can make it say a device is gone.</summary>
internal sealed class FakePushSender : IPushSender
{
    private readonly Lock _gate = new();

    public bool Enabled { get; set; } = true;

    public string PublicKey => "test-public-key";

    public PushOutcome Outcome { get; set; } = PushOutcome.Sent;

    public List<(string Endpoint, string Payload)> Sent { get; } = [];

    public Task<PushOutcome> SendAsync(PushTarget target, string payloadJson, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Sent.Add((target.Endpoint, payloadJson));
        }

        return Task.FromResult(Outcome);
    }

    public (string Endpoint, string Payload)[] SentTo(string endpoint)
    {
        lock (_gate)
        {
            return [.. Sent.Where(s => s.Endpoint == endpoint)];
        }
    }
}
