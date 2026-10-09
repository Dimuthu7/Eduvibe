using EduVibe.Api;
using EduVibe.Shared.Auditing;
using EduVibe.Shared.Geography;
using EduVibe.Shared.Persistence;
using EduVibe.Shared.Tenancy;
using System.Threading.RateLimiting;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var modules = ModuleCatalog.All;

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddEduVibePersistence(builder.Configuration);
builder.Services.AddEduVibeSecurity(builder.Configuration);

// Sign-in endpoints are limited per client address to slow down password guessing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var perMinute = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 10);
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
});

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
    app.MapOpenApi().AllowAnonymous();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is up. Readiness: the database is reachable too.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapGet("/api/system/info", () => new SystemInfo(
        "EduVibe",
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        app.Environment.EnvironmentName,
        modules.Select(m => m.Name).ToArray()))
    .WithTags("System")
    .AllowAnonymous();

app.MapGet("/api/system/districts", () => SriLankaDistricts.All).WithTags("System");

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();

public partial class Program;
