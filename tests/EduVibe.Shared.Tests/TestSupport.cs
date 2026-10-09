using EduVibe.Shared.Auditing;
using EduVibe.Shared.Domain;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EduVibe.Shared.Tests;

public sealed class FakeTenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public bool IsSuperAdmin { get; set; }
}

public sealed class Widget : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public override string Schema => "test";

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void ConfigureModule(ModelBuilder modelBuilder) => modelBuilder.Entity<Widget>();
}

/// <summary>An in-memory SQLite database shared by several contexts, each acting as a different user.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDatabase()
    {
        _connection.Open();

        // In production AuditDbContext owns the audit table and module contexts exclude it
        // from their migrations, so the test database creates the two sets of tables separately.
        using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(_connection).Options);
        audit.Database.EnsureCreated();

        using var context = CreateContext(new FakeTenantContext { IsSuperAdmin = true });
        context.GetService<IRelationalDatabaseCreator>().CreateTables();
    }

    public TestDbContext CreateContext(FakeTenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditingInterceptor(tenant, TimeProvider.System))
            .Options;
        return new TestDbContext(options, tenant);
    }

    public void Dispose() => _connection.Dispose();
}
