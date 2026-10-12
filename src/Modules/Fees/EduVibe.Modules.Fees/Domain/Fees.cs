using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;

namespace EduVibe.Modules.Fees.Domain;

/// <summary>
/// What one enrolment owes for one month. The amount is fixed when the due is made (class fee minus the enrolment's
/// discount, or nothing for a free card), so a later change to the class fee does not rewrite months already billed.
/// EnrolmentId and ClassId belong to other modules and are kept as plain ids.
/// </summary>
public sealed class FeeDue : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid EnrolmentId { get; set; }
    public Guid ClassId { get; set; }

    /// <summary>The fee month as "yyyy-MM".</summary>
    public required string Month { get; set; }

    /// <summary>LKR. Zero for a free card.</summary>
    public decimal AmountDue { get; set; }
}

public enum PaymentMethod
{
    Cash,
    BankTransfer,
    Online,
}

/// <summary>Money taken from a student. Never deleted: a mistake is voided with a reason and stays on record.</summary>
public sealed class Payment : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid EnrolmentId { get; set; }

    /// <summary>Unique within the teacher, for example "R-000012".</summary>
    public required string ReceiptNo { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateOnly PaidOn { get; set; }

    /// <summary>The signed-in user who recorded it.</summary>
    public Guid? ReceivedBy { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    public bool IsVoid => VoidedAt is not null;
}

/// <summary>The part of a payment that went toward one month. One payment can cover several months.</summary>
public sealed class PaymentAllocation : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid FeeDueId { get; set; }
    public decimal Amount { get; set; }
}

public enum MonthStatus
{
    Paid,
    PartlyPaid,
    Unpaid,
    FreeCard,
}
