using GuruBook.Shared.Auditing;
using GuruBook.Shared.Domain;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Students.Domain;

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

/// <summary>
/// A student's profile photo, so a teacher can tell students apart by face. The app shrinks it to a small square before sending,
/// so it is kept in the database next to the student and travels with backups and with erasure. At most one per student.
/// It sits in its own table so lists of students never load the bytes.
/// </summary>
public sealed class StudentPhoto : Entity
{
    public Guid StudentId { get; set; }

    /// <summary>"image/jpeg", "image/png" or "image/webp", read from the bytes and never taken from the request.</summary>
    public required string ContentType { get; set; }

    public required byte[] Data { get; set; }
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

/// <summary>
/// A teacher's grant letting one parent account see one child's fees, attendance and published results for that teacher's classes.
/// It is made on purpose, by the teacher, from the student's page: a guardian phone number alone never opens a child's records.
/// ParentUserId is the Parent account in the Identity module, kept as a plain id.
/// </summary>
public sealed class ParentLink : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }
    public Guid ParentUserId { get; set; }
}

/// <summary>
/// The code printed as a QR on one teacher's card for one student. It is random, so it cannot be guessed from the student's
/// details, and each teacher has their own: a teacher can only look up their own cards, and replacing a lost card changes the code
/// for that teacher alone. It proves nothing but "this card was handed to this student by this teacher", and only opens attendance marking.
/// </summary>
public sealed class StudentCard : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }

    /// <summary>22 URL-safe letters and digits (16 random bytes).</summary>
    public required string Code { get; set; }
}
