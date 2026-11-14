using GuruBook.Api;
using GuruBook.Shared.Auditing;
using GuruBook.Shared.Geography;
using GuruBook.Shared.Persistence;
using GuruBook.Shared.Tenancy;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var modules = ModuleCatalog.All;

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddGuruBookPersistence(builder.Configuration);
builder.Services.AddGuruBookSecurity(builder.Configuration);

builder.Services.AddGuruBookRateLimits(builder.Configuration);

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

// Behind a host's proxy (Render, Azure) the real client address comes from X-Forwarded-For; the sign-in rate limit needs it.
if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await MigrationRunner.MigrateAllAsync(app.Services);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// The API only answers with data, never pages, so browsers are told not to guess types, frame it, run anything from it or pass on its addresses.
app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; sandbox";
    return next(context);
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseCors();
app.UseAuthentication();

// After sign-in is read, so limits can count per person; before authorization, so refused requests count too.
app.UseRateLimiter();
app.UseAuthorization();

// Liveness: the process is up. Readiness: the database is reachable too.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapGet("/api/system/info", () => new SystemInfo(
        "GuruBook",
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
