namespace EduVibe.Shared.Domain;

/// <summary>Marks an entity whose creates, updates and deletes are written to the audit log.</summary>
public interface IAuditable;

/// <summary>
/// For an auditable entity that is about a teacher but is not owned by one (a plan subscription the Super Admin manages):
/// its audit rows are filed under this teacher, so the audit log can be filtered by teacher.
/// </summary>
public interface IAuditedForTenant
{
    Guid AuditTenantId { get; }
}
