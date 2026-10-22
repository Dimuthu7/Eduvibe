namespace EduVibe.Modules.WhatsApp;

/// <summary>
/// WhatsApp settings, from the "WhatsApp" configuration section. Mode is Off until someone sets the server up: Meta sends through the WhatsApp
/// Cloud API, Simulated pretends to (for demos and the free test site) so the whole flow can be tried without a Meta account.
/// docs/whatsapp-setup.md says where each value comes from.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    public string Mode { get; set; } = "Off";

    /// <summary>The sending number's id in Meta's WhatsApp Manager (not the phone number itself).</summary>
    public string PhoneNumberId { get; set; } = "";

    /// <summary>A permanent (system user) access token with the whatsapp_business_messaging permission.</summary>
    public string AccessToken { get; set; } = "";

    /// <summary>The text typed into Meta's webhook set-up so Meta can prove it is calling this server.</summary>
    public string VerifyToken { get; set; } = "";

    /// <summary>The Meta app's secret, used to check that a webhook call really came from Meta.</summary>
    public string AppSecret { get; set; } = "";

    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>The language code the templates were approved in.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Template names are this plus the message kind, for example gurubook_fee_reminder.</summary>
    public string TemplatePrefix { get; set; } = "gurubook_";

    public bool IsSimulated => string.Equals(Mode, "Simulated", StringComparison.OrdinalIgnoreCase);

    public bool IsMeta => string.Equals(Mode, "Meta", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => IsSimulated || (IsMeta && PhoneNumberId != "" && AccessToken != "");
}
