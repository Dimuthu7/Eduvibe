using GuruBook.Shared.Domain;

namespace GuruBook.Modules.Notifications.Domain;

/// <summary>
/// One phone or browser that agreed to get notifications. The endpoint is the address the browser's push service gave it, so it is unique:
/// signing in as someone else on the same phone moves the device to them. A teacher is told by teacher id and a student by account id,
/// so a device carries both and is found by either.
/// </summary>
public sealed class PushDevice : Entity
{
    public Guid UserId { get; set; }

    /// <summary>The teacher id (tenant id) when the person is a teacher.</summary>
    public Guid? TenantId { get; set; }

    public required string Endpoint { get; set; }

    /// <summary>The device's public key and auth secret (base64url), needed to encrypt what is sent to it.</summary>
    public required string P256dh { get; set; }
    public required string Auth { get; set; }

    /// <summary>The language on screen when the device was last seen, so the phone shows the words in it.</summary>
    public string Language { get; set; } = "en";

    public DateTimeOffset LastSeenAt { get; set; }
}
