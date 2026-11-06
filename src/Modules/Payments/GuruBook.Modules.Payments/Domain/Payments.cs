using GuruBook.Shared.Auditing;
using GuruBook.Shared.Domain;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Payments.Domain;

/// <summary>
/// Where a teacher's students send money: one row per teacher. Shown only to their own students and the parents they linked, never to
/// another teacher. Nothing here is a secret the app has to keep, but it is still a teacher's own details, so it goes out only through
/// the screens that have checked who is asking.
/// </summary>
public sealed class BankDetails : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string BankName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string Branch { get; set; } = "";

    /// <summary>Anything the teacher wants the payer to know, for example which name to write on the transfer.</summary>
    public string Note { get; set; } = "";

    public bool IsFilled => BankName.Length > 0 && AccountName.Length > 0 && AccountNumber.Length > 0;
}

/// <summary>The teacher's LankaQR image, kept out of <see cref="BankDetails"/> so reading the details never drags the bytes along.</summary>
public sealed class PayQr : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public required string ContentType { get; set; }

    public required byte[] Data { get; set; }
}

public enum ClaimStatus
{
    /// <summary>The student says they have paid; the teacher has not looked yet.</summary>
    Open,
    Confirmed,
    Rejected,
}

/// <summary>
/// "I've paid": a student or parent says a fee went out by bank transfer or LankaQR, with a slip photo or the reference they used. One fee
/// can have one open claim at a time. Confirming it records an ordinary bank transfer payment in fees; rejecting it says why. The claim is
/// kept either way, so there is a record of what was said, but the slip image is cleared some months after the decision.
/// </summary>
public sealed class PaymentClaim : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid EnrolmentId { get; set; }
    public Guid StudentId { get; set; }
    public Guid ClassId { get; set; }

    /// <summary>The fee this is for, and the month it covers, as "yyyy-MM".</summary>
    public Guid FeeDueId { get; set; }

    public required string Month { get; set; }

    /// <summary>LKR the payer says they sent.</summary>
    public decimal Amount { get; set; }

    /// <summary>The short code the pay screen asked them to write on the transfer, so a bank line can be matched to a student.</summary>
    public required string Reference { get; set; }

    /// <summary>What the payer typed: their own bank reference, or a note about the transfer. Empty when they only sent a slip.</summary>
    public string Note { get; set; } = "";

    public string? SlipContentType { get; set; }

    /// <summary>The slip photo. Kept out of the audit log: a bank slip is the payer's own document, and the bytes would swamp the log.</summary>
    [NotAudited]
    public byte[]? Slip { get; set; }

    /// <summary>When the slip image was cleared, months after the decision. The claim itself stays.</summary>
    public DateTimeOffset? SlipRemovedAt { get; set; }

    public ClaimStatus Status { get; set; } = ClaimStatus.Open;

    /// <summary>The account that made the claim, and whether that was a parent rather than the student themselves.</summary>
    public Guid ClaimedBy { get; set; }

    public bool ByParent { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? RejectReason { get; set; }

    /// <summary>The fees payment this became, once confirmed.</summary>
    public Guid? PaymentId { get; set; }

    public string? ReceiptNo { get; set; }

    public bool HasSlip => Slip is { Length: > 0 };
}
