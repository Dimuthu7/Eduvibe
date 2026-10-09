using EduVibe.Shared.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Shared.Persistence;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "EduVibe";

    /// <summary>Registers a module's DbContext on the shared PostgreSQL database, in its own schema.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
            UsePostgres(options, configuration, schema)
                .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
        services.AddSingleton(new MigratableContext(typeof(TContext)));
        return services;
    }

    /// <summary>Registers the audit context and the interceptor every module context uses.</summary>
    public static IServiceCollection AddEduVibePersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AuditDbContext>(options => UsePostgres(options, configuration, AuditDbContext.Schema));
        services.AddSingleton(new MigratableContext(typeof(AuditDbContext)));
        return services;
    }

    private static DbContextOptionsBuilder UsePostgres(DbContextOptionsBuilder options, IConfiguration configuration, string schema) =>
        options
            .UseNpgsql(
                configuration.GetConnectionString(ConnectionStringName),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();
}

/// <summary>A DbContext type whose migrations are applied by <see cref="MigrationRunner"/>.</summary>
public sealed record MigratableContext(Type ContextType);
