namespace GuruBook.Modules.Sms;

/// <summary>
/// SMS settings, from the "Sms" configuration section. Mode is Off until someone sets the server up: TextLk sends through text.lk, Simulated
/// pretends to (for demos and the free test site) so the whole flow can be tried without an account and without paying for messages.
/// docs/sms-setup.md says where each value comes from.
/// </summary>
public sealed class SmsOptions
{
    public const string Section = "Sms";

    public string Mode { get; set; } = "Off";

    /// <summary>The API token from the text.lk dashboard.</summary>
    public string ApiToken { get; set; } = "";

    /// <summary>The approved sender name shown on the parent's phone, for example GuruBook. text.lk calls it the Sender ID.</summary>
    public string SenderId { get; set; } = "GuruBook";

    public string BaseUrl { get; set; } = "https://app.text.lk";

    public bool IsSimulated => string.Equals(Mode, "Simulated", StringComparison.OrdinalIgnoreCase);

    public bool IsTextLk => string.Equals(Mode, "TextLk", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => IsSimulated || (IsTextLk && ApiToken != "" && SenderId != "");
}
