using GuruBook.Shared.Domain;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Sms.Domain;

public enum SmsStatus
{
    /// <summary>Waiting for the background sender.</summary>
    Queued,
    Sent,
    Delivered,
    Failed,
}

/// <summary>
/// One SMS a teacher asked for, and what happened to it. The log doubles as the monthly allowance (Failed ones do not count) and as the memory
/// that stops the same reminder going out twice. The words are kept because a teacher pays for them and should be able to see what was sent.
/// </summary>
public sealed class SmsMessage : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }

    /// <summary>The student's name when the message was queued, shown in the list. Removed when the student is erased with the rest of the row.</summary>
    public required string StudentName { get; set; }

    /// <summary>One of <c>WhatsAppKinds</c>: the same kinds, because the same facts can go by either channel.</summary>
    public required string Kind { get; set; }

    /// <summary>International form, for example +94771234567: the guardian's number when the student has one.</summary>
    public required string ToPhone { get; set; }

    /// <summary>The words as they go out.</summary>
    public required string Text { get; set; }

    /// <summary>The language the text is in ("en", "si" or "ta").</summary>
    public required string Language { get; set; }

    /// <summary>How many 160-letter (70 for Sinhala and Tamil) parts the text takes: what the teacher pays for and what the allowance counts.</summary>
    public int Parts { get; set; }

    public required string DedupKey { get; set; }

    /// <summary>The Sri Lanka month it was queued in, "yyyy-MM": what the plan's monthly allowance counts.</summary>
    public required string Month { get; set; }

    /// <summary>True when WhatsApp could not deliver the message and this text took over, so the teacher can see why they were charged.</summary>
    public bool Fallback { get; set; }

    public SmsStatus Status { get; set; }
    public string? ProviderMessageId { get; set; }

    /// <summary>A short code such as bad_number or no_credit when Status is Failed; the web app turns it into words.</summary>
    public string? Error { get; set; }

    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>
/// A number that is to get no more texts. Unlike WhatsApp's, this one is not tied to a teacher: an SMS arrives with no way to reply STOP to the
/// right teacher, so a parent who says stop means every teacher, and that is also the kinder reading of the law.
/// </summary>
public sealed class SmsOptOut : Entity
{
    public required string Phone { get; set; }

    /// <summary>True when the parent turned it off themselves in the app, false when a teacher did it for them.</summary>
    public bool ByOwner { get; set; }
}

/// <summary>How one teacher's paid messages go out. Missing means the default, WhatsApp first and SMS for whoever is not on WhatsApp.</summary>
public sealed class SmsSetting : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>One of <see cref="GuruBook.Shared.Modules.MessageChannels"/>.</summary>
    public required string Channel { get; set; }
}
