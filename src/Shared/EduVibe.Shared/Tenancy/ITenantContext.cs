namespace EduVibe.Shared.Tenancy;

/// <summary>Who is making the current request, as far as data access is concerned.</summary>
public interface ITenantContext
{
    /// <summary>The teacher whose data may be read and written, or null when there is none.</summary>
    Guid? TenantId { get; }

    /// <summary>The signed-in user, or null for anonymous requests and background jobs.</summary>
    Guid? UserId { get; }

    /// <summary>Super Admins see every tenant's data.</summary>
    bool IsSuperAdmin { get; }
}
