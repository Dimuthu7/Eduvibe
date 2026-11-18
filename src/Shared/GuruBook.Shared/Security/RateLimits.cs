using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Shared.Security;

/// <summary>
/// How often a request may be made. Every signed-in person has a roomy limit across the whole API, so a runaway script or a stolen
/// token cannot flood the server, and a few endpoints add a tighter limit of their own. Limits per address apply before sign-in;
/// limits per person use the signed-in user. Each limit is per minute and can be changed under RateLimit in configuration.
/// </summary>
public static class RateLimits
{
    /// <summary>Signing in and joining through a class link: per address, to slow down password guessing.</summary>
    public const string SignIn = "auth";

    /// <summary>Keeping a session going (refresh, sign out): per address, roomy enough for a full class on one Wi-Fi.</summary>
    public const string Session = "session";

    /// <summary>Changing a password or handing out a one-time password: per person.</summary>
    public const string Passwords = "passwords";

    /// <summary>Photos, payment slips, screenshots and class files: per person.</summary>
    public const string Uploads = "uploads";

    public static IServiceCollection AddGuruBookRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        int PerMinute(string key, int fallback) => configuration.GetValue($"RateLimit:{key}", fallback);

        var signIn = PerMinute("AuthPerMinute", 10);
        var session = PerMinute("SessionPerMinute", 60);
        var passwords = PerMinute("PasswordsPerMinute", 10);
        var uploads = PerMinute("UploadsPerMinute", 30);
        var perPerson = PerMinute("UserPerMinute", 600);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            // Requests without a sign-in are not counted here: the ones that matter have a limit per address, and the WhatsApp
            // webhook must take Meta's bursts of delivery reports.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.User.FindFirst(GuruBookClaims.Subject)?.Value is { } user
                    ? RateLimitPartition.GetFixedWindowLimiter("user:" + user, _ => Window(perPerson))
                    : RateLimitPartition.GetNoLimiter("anonymous"));

            options.AddPolicy(SignIn, context => RateLimitPartition.GetFixedWindowLimiter(Address(context), _ => Window(signIn)));
            options.AddPolicy(Session, context => RateLimitPartition.GetFixedWindowLimiter(Address(context), _ => Window(session)));
            options.AddPolicy(Passwords, context => RateLimitPartition.GetFixedWindowLimiter(Person(context), _ => Window(passwords)));
            options.AddPolicy(Uploads, context => RateLimitPartition.GetFixedWindowLimiter(Person(context), _ => Window(uploads)));
        });
    }

    private static FixedWindowRateLimiterOptions Window(int perMinute) => new() { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) };

    private static string Address(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // These endpoints need a sign-in, which is checked after the limit; a request without one is counted by address and then refused.
    private static string Person(HttpContext context) =>
        context.User.FindFirst(GuruBookClaims.Subject)?.Value is { } user ? "user:" + user : "address:" + Address(context);
}
