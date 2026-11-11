namespace GuruBook.Modules.Notifications.Push;

/// <summary>
/// The keys that let the API send phone notifications (web push, "VAPID"). Without both keys phone notifications are off and the app does not
/// offer them. Set Push:PublicKey and Push:PrivateKey (see docs/push-setup.md); Push:Subject is a contact address or site the push services can reach.
/// </summary>
public sealed class PushOptions
{
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "https://gurubook-web.dimuthuc002.workers.dev";

    /// <summary>
    /// The browsers' push services. A device address must be on one of these hosts (or below them), so a signed-in user cannot make the
    /// server send requests to any address they like. Push:ServiceHosts adds hosts to the list, for example in tests.
    /// </summary>
    public string[] ServiceHosts { get; set; } =
    [
        "fcm.googleapis.com",           // Chrome, Android, Samsung Internet, Opera
        "push.services.mozilla.com",    // Firefox
        "notify.windows.com",           // Edge
        "push.apple.com",               // Safari, iPhone home-screen apps
    ];

    public bool Enabled => PublicKey.Length > 0 && PrivateKey.Length > 0;

    /// <summary>True when the address is https on a push service host, on the default port.</summary>
    public bool IsPushService(Uri endpoint) =>
        endpoint.Scheme == Uri.UriSchemeHttps && endpoint.IsDefaultPort && endpoint.UserInfo.Length == 0
        && ServiceHosts.Any(host => endpoint.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase)
            || endpoint.IdnHost.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
}
