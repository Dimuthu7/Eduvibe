using EduVibe.Api;
using EduVibe.Shared.Auditing;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var modules = ModuleCatalog.All;

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddEduVibePersistence(builder.Configuration);

foreach (var module in modules)
{
    module.AddServices(builder.Services, builder.Configuration);
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AuditDbContext>("database", tags: ["ready"]);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await MigrationRunner.MigrateAllAsync(app.Services);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// Liveness: the process is up. Readiness: the database is reachable too.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapGet("/api/system/info", () => new SystemInfo(
        "EduVibe",
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        app.Environment.EnvironmentName,
        modules.Select(m => m.Name).ToArray()))
    .WithTags("System");

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();

public partial class Program;
