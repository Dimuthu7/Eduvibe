namespace GuruBook.Modules.Notifications.Push;

/// <summary>Where to send: the address a browser's push service gave a device, and the keys to encrypt for it.</summary>
internal sealed record PushTarget(string Endpoint, string P256dh, string Auth);

internal enum PushOutcome
{
    Sent,

    /// <summary>The push service says the device is gone (the person turned notifications off or removed the app). Forget it.</summary>
    Gone,

    /// <summary>A temporary problem. The notification is still in the app's list, so nothing more is done.</summary>
    Failed,
}

/// <summary>Sends one encrypted message to one device. A fake replaces it in tests.</summary>
internal interface IPushSender
{
    bool Enabled { get; }

    /// <summary>The public key the app needs to subscribe a device.</summary>
    string PublicKey { get; }

    Task<PushOutcome> SendAsync(PushTarget target, string payloadJson, CancellationToken cancellationToken);
}
