using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;

namespace EduVibe.Modules.WhatsApp.Domain;

public enum MessageStatus
{
    /// <summary>Waiting for the background sender.</summary>
    Queued,
    Sent,
    Delivered,
    Read,
    Failed,
}

/// <summary>
/// One WhatsApp message a teacher asked for, and what happened to it. The log doubles as the monthly allowance (Failed ones do not count)
/// and as the memory that stops the same reminder going out twice.
/// </summary>
public sealed class WhatsAppMessage : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }

    /// <summary>The student's name when the message was queued, shown in the list. Removed when the student is erased with the rest of the row.</summary>
    public required string StudentName { get; set; }

    /// <summary>One of <c>WhatsAppKinds</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>International form, for example +94771234567: the guardian's number when the student has one.</summary>
    public required string ToPhone { get; set; }

    /// <summary>The template's words, as a JSON array of strings.</summary>
    public required string ParamsJson { get; set; }

    public required string DedupKey { get; set; }

    /// <summary>The Sri Lanka month it was queued in, "yyyy-MM": what the plan's monthly allowance counts.</summary>
    public required string Month { get; set; }

    public MessageStatus Status { get; set; }
    public string? ProviderMessageId { get; set; }

    /// <summary>A short code such as not_on_whatsapp or template_problem when Status is Failed; the web app turns it into words.</summary>
    public string? Error { get; set; }

    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>A parent who replied STOP: this teacher sends nothing more to the number, until they reply START.</summary>
public sealed class WhatsAppOptOut : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Phone { get; set; }
}
