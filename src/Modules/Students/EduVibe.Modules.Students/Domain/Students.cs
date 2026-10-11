using EduVibe.Shared.Auditing;
using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;

namespace EduVibe.Modules.Students.Domain;

/// <summary>
/// A student, shared by every teacher who teaches them. The phone number is the identity; a teacher sees
/// a student only through an enrolment in one of their own classes. UserId is the sign-in account in the
/// Identity module, kept as a plain id.
/// </summary>
public sealed class Student : Entity, IAuditable
{
    public required string Phone { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? School { get; set; }
    public string? Grade { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    public Guid? UserId { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public enum EnrolmentStatus
{
    /// <summary>Asked to join through an invite link; waits for the teacher to approve.</summary>
    Pending,
    Active,
    Left,
}

public enum FeePlan
{
    Full,
    Discount,
    FreeCard,
}

/// <summary>One student in one class. ClassId belongs to the Classes module and is kept as a plain id.</summary>
public sealed class Enrolment : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public Guid ClassId { get; set; }

    /// <summary>The student's number in this class, unique within the class.</summary>
    public int IndexNumber { get; set; }

    public DateOnly JoinedOn { get; set; }
    public DateOnly? LeftOn { get; set; }
    public EnrolmentStatus Status { get; set; }
    public FeePlan FeePlan { get; set; }

    /// <summary>LKR taken off the class's monthly fee when FeePlan is Discount; zero otherwise.</summary>
    public decimal DiscountAmount { get; set; }
}

/// <summary>A link a teacher shares so students can ask to join one class.</summary>
public sealed class InviteLink : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }

    [NotAudited]
    public required string Token { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
