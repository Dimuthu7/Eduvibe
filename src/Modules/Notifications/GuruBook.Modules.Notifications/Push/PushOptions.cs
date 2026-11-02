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

    public bool Enabled => PublicKey.Length > 0 && PrivateKey.Length > 0;
}
