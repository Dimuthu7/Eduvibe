namespace EduVibe.Shared.Tenancy;

/// <summary>
/// Marks an entity that belongs to one teacher (the tenant). Queries on these entities are
/// filtered to the current tenant automatically, and the tenant is stamped on insert.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
