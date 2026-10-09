using Microsoft.EntityFrameworkCore;

namespace EduVibe.Shared.Auditing;

/// <summary>Owns the audit schema and its migrations. Modules write audit rows through their own context.</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditLogEntry> Entries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AuditLogEntry>(AuditLogEntryMapping.Configure);
    }
}

internal static class AuditLogEntryMapping
{
    public const string Table = "audit_log";

    public static void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AuditLogEntry> entity)
    {
        entity.ToTable(Table, AuditDbContext.Schema);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Entity).HasMaxLength(200);
        entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
        entity.Property(e => e.Before).HasColumnType("jsonb");
        entity.Property(e => e.After).HasColumnType("jsonb");
        entity.HasIndex(e => new { e.TenantId, e.At });
        entity.HasIndex(e => new { e.Entity, e.EntityId });
    }
}
