namespace EduVibe.Shared.Auditing;

/// <summary>Keeps a property out of the audit log, for secrets such as password hashes.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;
