using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduVibe.Modules.Catalog;
using EduVibe.Modules.Catalog.Domain;
using EduVibe.Modules.Classes;
using EduVibe.Modules.Identity;
using EduVibe.Modules.Identity.Domain;
using EduVibe.Modules.Identity.Services;
using EduVibe.Shared.Auditing;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EduVibe.Api.Tests;

/// <summary>Runs the real API on an in-memory SQLite database, so tests need no PostgreSQL.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPhone = "+94770000001";
    public const string AdminPassword = "AdminPass123";

    /// <summary>A second Super Admin nobody has signed in as, for tests of the first sign-in.</summary>
    public const string FreshAdminPhone = "+94770000002";

    private static readonly SemaphoreSlim AdminLock = new(1, 1);
    private HttpClient? _admin;

    /// <summary>A Super Admin client that has already replaced the one-time password. Created once per factory.</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        await AdminLock.WaitAsync();
        try
        {
            if (_admin is null)
            {
                var login = await CreateClient().PostAsJsonAsync("/api/identity/login", new { phone = AdminPhone, password = AdminPassword });
                var first = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
                var anon = CreateClient();
                anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first);
                var changed = await anon.PostAsJsonAsync("/api/identity/change-password", new { currentPassword = AdminPassword, newPassword = AdminPassword + "x" });
                var token = (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
                _admin = CreateClient();
                _admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return _admin;
        }
        finally
        {
            AdminLock.Release();
        }
    }

    /// <summary>Catalog rows every test can use when it creates a teacher.</summary>
    public Guid StreamId { get; private set; }
    public Guid MathsId { get; private set; }
    public Guid ScienceId { get; private set; }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-test-signing-key-0123456789");
        builder.UseSetting("RateLimit:AuthPerMinute", "1000");
        builder.UseSetting("Catalog:SeedOnStartup", "false");

        builder.ConfigureServices(services =>
        {
            UseSqlite<IdentityDbContext>(services);
            UseSqlite<CatalogDbContext>(services);
            UseSqlite<ClassesDbContext>(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();

        // The audit table belongs to AuditDbContext; module contexts exclude it from their tables.
        var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(_connection).Options);
        audit.Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<IdentityDbContext>().GetService<IRelationalDatabaseCreator>().CreateTables();
        scope.ServiceProvider.GetRequiredService<CatalogDbContext>().GetService<IRelationalDatabaseCreator>().CreateTables();
        scope.ServiceProvider.GetRequiredService<ClassesDbContext>().GetService<IRelationalDatabaseCreator>().CreateTables();

        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stream = new EducationStream { Name = "O/L", SortOrder = 10 };
        var maths = new Subject { Name = "Mathematics", SortOrder = 10 };
        var science = new Subject { Name = "Science", SortOrder = 20 };
        catalog.Streams.Add(stream);
        catalog.Subjects.AddRange(maths, science);
        catalog.SaveChanges();
        StreamId = stream.Id;
        MathsId = maths.Id;
        ScienceId = science.Id;

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
        foreach (var phone in new[] { AdminPhone, FreshAdminPhone })
        {
            var admin = new User { Phone = phone, FirstName = "Test", LastName = "Admin", MustChangePassword = true };
            admin.PasswordHash = passwords.Hash(admin, AdminPassword);
            admin.Roles.Add(new UserRole { Role = Roles.SuperAdmin });
            db.Users.Add(admin);
        }

        db.SaveChanges();
        return host;
    }

    private void UseSqlite<TContext>(IServiceCollection services) where TContext : DbContext
    {
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.RemoveAll<DbContextOptions<TContext>>();
        services.AddDbContext<TContext>((sp, options) => options
            .UseSqlite(_connection)
            .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
