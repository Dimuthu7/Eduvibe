namespace EduVibe.Modules.Materials.Drive;

/// <summary>
/// The Google OAuth client GuruBook uses, from the "Google" configuration section. Without a client id and secret the feature reports
/// itself as not set up and nothing calls Google. See docs/drive-setup.md for how to create the client.
/// </summary>
public sealed class DriveOptions
{
    public const string Section = "Google";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>The API's callback address, exactly as registered with Google, for example https://api.example.com/api/materials/drive/callback.</summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>Where the browser goes after connecting: the web app's address. Falls back to the first allowed CORS origin.</summary>
    public string WebReturnUrl { get; set; } = "";

    public bool IsConfigured => ClientId != "" && ClientSecret != "" && RedirectUri != "";
}
