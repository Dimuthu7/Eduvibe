namespace EduVibe.Shared.Auditing;

/// <summary>One change to an auditable entity. Rows are only ever appended.</summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public required string Entity { get; init; }
    public required Guid EntityId { get; init; }
    public required AuditAction Action { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public DateTimeOffset At { get; init; }
}

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
}
