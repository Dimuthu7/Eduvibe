using EduVibe.Shared.Domain;
using EduVibe.Shared.Auditing;

namespace EduVibe.Modules.Identity.Domain;

/// <summary>A person who can sign in. The phone number is the identity; email is optional contact detail.</summary>
public sealed class User : Entity, IAuditable
{
    public required string Phone { get; set; }
    public required string FullName { get; set; }
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
    public string? Town { get; set; }
    public string? Subjects { get; set; }
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
