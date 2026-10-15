using EduVibe.Shared.Domain;

namespace EduVibe.Modules.Plans.Domain;

/// <summary>
/// What a teacher can use. The Super Admin makes plans and picks what each includes; exactly one plan is the default,
/// which teachers are on until one is assigned.
/// </summary>
public sealed class Plan : Entity, IAuditable
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>The plan every teacher starts on. It cannot be switched off.</summary>
    public bool IsDefault { get; set; }

    /// <summary>A plan that is switched off cannot be assigned to more teachers; teachers already on it keep it.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>LKR per month. Plans are free to start with, so this only records what the Super Admin intends to charge.</summary>
    public decimal MonthlyPrice { get; set; }
}

/// <summary>
/// One key of the fixed list on one plan: whether a feature is on, or the cap of a limit. Every plan holds a row for every key.
/// For a limit, Enabled is always true and a null LimitValue means no cap.
/// </summary>
public sealed class PlanFeature : Entity, IAuditable
{
    public Guid PlanId { get; set; }
    public required string Key { get; set; }
    public bool Enabled { get; set; }
    public int? LimitValue { get; set; }
}

public enum SubscriptionStatus
{
    Active,

    /// <summary>Replaced by a plan that starts on or before it did, so it never took effect.</summary>
    Cancelled,
}

/// <summary>
/// A teacher on a plan from a date, until an optional end date. One is in effect at a time; a teacher with none in effect is on the default plan.
/// TeacherId is the teacher's id from the Identity module.
/// </summary>
public sealed class TeacherSubscription : Entity, IAuditable, IAuditedForTenant
{
    public Guid TeacherId { get; set; }
    Guid IAuditedForTenant.AuditTenantId => TeacherId;
    public Guid PlanId { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public string? Note { get; set; }
}

/// <summary>A payment the Super Admin recorded by hand for a teacher's subscription.</summary>
public sealed class PlanPayment : Entity, IAuditable, IAuditedForTenant
{
    public Guid SubscriptionId { get; set; }
    public Guid TeacherId { get; set; }
    Guid IAuditedForTenant.AuditTenantId => TeacherId;
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }

    /// <summary>cash, bank_transfer or other.</summary>
    public required string Method { get; set; }
    public string? Reference { get; set; }
    public Guid? RecordedBy { get; set; }
}
