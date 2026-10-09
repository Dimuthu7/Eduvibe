using System.Linq.Expressions;
using EduVibe.Shared.Auditing;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Shared.Persistence;

/// <summary>
/// Base for each module's DbContext. Each module owns one PostgreSQL schema, and every
/// tenant-owned entity is filtered to the current tenant unless the caller is a Super Admin.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, ITenantContext tenant) : DbContext(options)
{
    /// <summary>The PostgreSQL schema this module owns, for example "fees".</summary>
    public abstract string Schema { get; }

    // Read by the query filters on every query, so the filter follows the current request.
    protected Guid? CurrentTenantId => tenant.TenantId;
    protected bool SeesAllTenants => tenant.IsSuperAdmin;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // The audit table belongs to AuditDbContext; modules only add rows to it.
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            AuditLogEntryMapping.Configure(entity);
            entity.ToTable(AuditLogEntryMapping.Table, AuditDbContext.Schema, t => t.ExcludeFromMigrations());
        });

        ConfigureModule(modelBuilder);
        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>Configure this module's entities.</summary>
    protected abstract void ConfigureModule(ModelBuilder modelBuilder);

    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType) || entityType.BaseType is not null)
            {
                continue;
            }

            entityType.SetQueryFilter(BuildTenantFilter(entityType.ClrType));
            entityType.AddIndex(entityType.FindProperty(nameof(ITenantOwned.TenantId))!);
        }
    }

    // e => this.SeesAllTenants || (Guid?)e.TenantId == this.CurrentTenantId
    private LambdaExpression BuildTenantFilter(Type clrType)
    {
        var entity = Expression.Parameter(clrType, "e");
        var context = Expression.Constant(this);
        var seesAll = Expression.Property(context, nameof(SeesAllTenants));
        var current = Expression.Property(context, nameof(CurrentTenantId));
        var tenantId = Expression.Convert(Expression.Property(entity, nameof(ITenantOwned.TenantId)), typeof(Guid?));
        return Expression.Lambda(Expression.OrElse(seesAll, Expression.Equal(tenantId, current)), entity);
    }
}
