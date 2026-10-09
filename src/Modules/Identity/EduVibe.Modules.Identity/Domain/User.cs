using EduVibe.Shared.Domain;
using EduVibe.Shared.Auditing;

namespace EduVibe.Modules.Identity.Domain;

/// <summary>A person who can sign in. The phone number is the identity; the username is what they type to sign in. Email is optional contact detail.</summary>
public sealed class User : Entity, IAuditable
{
    public required string Phone { get; set; }

    /// <summary>Lowercase sign-in name chosen at first sign-in. Null until then; the phone number is the username before that.</summary>
    public string? Username { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string Language { get; set; } = "en";
    public bool IsActive { get; set; } = true;

    [NotAudited]
    public string PasswordHash { get; set; } = "";

    /// <summary>True while the password is a one-time password handed out by an admin.</summary>
    public bool MustChangePassword { get; set; }

    [NotAudited]
    public int FailedLoginCount { get; set; }

    [NotAudited]
    public DateTimeOffset? LockedUntil { get; set; }

    [NotAudited]
    public DateTimeOffset? LastLoginAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public List<UserRole> Roles { get; set; } = [];
}

public sealed class UserRole : Entity
{
    public Guid UserId { get; set; }
    public required string Role { get; set; }
}

/// <summary>
/// The teacher profile. Its id is the tenant id: every teacher-owned row carries it,
/// and the teacher's token holds it as the tenant_id claim.
/// </summary>
public sealed class Teacher : Entity, IAuditable
{
    public Guid UserId { get; set; }

    /// <summary>One of the districts in <c>SriLankaDistricts</c>.</summary>
    public string? District { get; set; }

    /// <summary>The teaching stream (O/L, A/L, ...) from the Catalog module. Only the id is kept here.</summary>
    public Guid? StreamId { get; set; }

    public List<TeacherSubject> Subjects { get; set; } = [];
}

/// <summary>A subject a teacher teaches. SubjectId belongs to the Catalog module.</summary>
public sealed class TeacherSubject : Entity
{
    public Guid TeacherId { get; set; }
    public Guid SubjectId { get; set; }
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
