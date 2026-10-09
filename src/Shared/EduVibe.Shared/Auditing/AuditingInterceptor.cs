using System.Text.Json;
using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduVibe.Shared.Auditing;

/// <summary>
/// Runs before every save: stamps timestamps, stamps the tenant on new tenant-owned rows,
/// blocks moving a row to another tenant, and adds audit rows in the same transaction.
/// </summary>
public sealed class AuditingInterceptor(ITenantContext tenant, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var audits = new List<AuditLogEntry>();

        foreach (var entry in context.ChangeTracker.Entries<Entity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    StampTenant(entry);
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    GuardTenant(entry);
                    break;
                case EntityState.Deleted:
                    break;
                default:
                    continue;
            }

            if (entry.Entity is IAuditable)
            {
                audits.Add(ToAudit(entry, now));
            }
        }

        if (audits.Count > 0)
        {
            context.Set<AuditLogEntry>().AddRange(audits);
        }
    }

    private void StampTenant(EntityEntry<Entity> entry)
    {
        if (entry.Entity is not ITenantOwned owned || owned.TenantId != Guid.Empty)
        {
            return;
        }

        owned.TenantId = tenant.TenantId
            ?? throw new InvalidOperationException(
                $"Cannot create {entry.Metadata.ClrType.Name} without a tenant.");
    }

    private static void GuardTenant(EntityEntry<Entity> entry)
    {
        if (entry.Entity is ITenantOwned && entry.Property(nameof(ITenantOwned.TenantId)).IsModified)
        {
            throw new InvalidOperationException(
                $"The tenant of {entry.Metadata.ClrType.Name} {entry.Entity.Id} cannot be changed.");
        }
    }

    private AuditLogEntry ToAudit(EntityEntry<Entity> entry, DateTimeOffset now)
    {
        var (action, before, after) = entry.State switch
        {
            EntityState.Added => (AuditAction.Created, null, Snapshot(entry, p => p.CurrentValue, all: true)),
            EntityState.Deleted => (AuditAction.Deleted, Snapshot(entry, p => p.OriginalValue, all: true), null),
            _ => (AuditAction.Updated, Snapshot(entry, p => p.OriginalValue, all: false), Snapshot(entry, p => p.CurrentValue, all: false)),
        };

        return new AuditLogEntry
        {
            TenantId = (entry.Entity as ITenantOwned)?.TenantId ?? tenant.TenantId,
            UserId = tenant.UserId,
            Entity = entry.Metadata.ClrType.Name,
            EntityId = entry.Entity.Id,
            Action = action,
            Before = before,
            After = after,
            At = now,
        };
    }

    private static string Snapshot(EntityEntry entry, Func<PropertyEntry, object?> value, bool all)
    {
        var values = entry.Properties
            .Where(p => all || p.IsModified)
            .Where(p => p.Metadata.PropertyInfo?.IsDefined(typeof(NotAuditedAttribute), inherit: true) != true)
            .ToDictionary(p => p.Metadata.Name, value);
        return JsonSerializer.Serialize(values);
    }
}
