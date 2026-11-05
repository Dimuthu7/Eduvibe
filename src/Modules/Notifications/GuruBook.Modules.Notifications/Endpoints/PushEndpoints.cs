using GuruBook.Modules.Notifications.Domain;
using GuruBook.Modules.Notifications.Push;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Notifications.Endpoints;

/// <summary>What the app needs to turn phone notifications on: whether the server can send them and its public key.</summary>
public sealed record PushSettingsDto(bool Enabled, string PublicKey);

/// <summary>A device's address and keys, exactly as the browser gives them, and the language on screen.</summary>
public sealed record PushDeviceRequest(string? Endpoint, string? P256dh, string? Auth, string? Language);

public sealed record PushEndpointRequest(string? Endpoint);

/// <summary>Turning phone notifications on and off for the signed-in person's own device.</summary>
internal static class PushEndpoints
{
    private static readonly string[] Languages = ["en", "si", "ta"];

    public static void Map(RouteGroupBuilder group)
    {
        var push = group.MapGroup("/push").RequireAuthorization();
        push.MapGet("", Settings);
        push.MapPut("/device", Save);
        push.MapPost("/unsubscribe", Remove);
    }

    private static IResult Settings(IPushSender sender) => Results.Ok(new PushSettingsDto(sender.Enabled, sender.Enabled ? sender.PublicKey : ""));

    private static async Task<IResult> Save(PushDeviceRequest request, NotificationsDbContext db, ITenantContext user, IPushSender sender, TimeProvider clock, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (!sender.Enabled)
        {
            return Problems.Reject("push_unavailable", StatusCodes.Status409Conflict);
        }

        if (!ValidEndpoint(request.Endpoint) || !Key(request.P256dh, 65, 0x04) || !Key(request.Auth, 16))
        {
            return Problems.Reject("push_device_invalid");
        }

        var language = Languages.Contains(request.Language) ? request.Language! : "en";
        var now = clock.GetUtcNow();

        // The same device signing in as someone else moves to them, so notifications never go to the previous person.
        var device = await db.PushDevices.SingleOrDefaultAsync(d => d.Endpoint == request.Endpoint, ct);
        if (device is null)
        {
            db.PushDevices.Add(new PushDevice
            {
                UserId = userId, TenantId = user.TenantId, Endpoint = request.Endpoint!, P256dh = request.P256dh!, Auth = request.Auth!,
                Language = language, LastSeenAt = now,
            });
        }
        else
        {
            device.UserId = userId;
            device.TenantId = user.TenantId;
            device.P256dh = request.P256dh!;
            device.Auth = request.Auth!;
            device.Language = language;
            device.LastSeenAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two requests added the same device at once; the other one has it.
            db.ChangeTracker.Clear();
        }

        return Results.NoContent();
    }

    /// <summary>Stops notifications to one device, only if it belongs to the signed-in person.</summary>
    private static async Task<IResult> Remove(PushEndpointRequest request, NotificationsDbContext db, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (!string.IsNullOrEmpty(request.Endpoint))
        {
            await db.PushDevices.Where(d => d.Endpoint == request.Endpoint && d.UserId == userId).ExecuteDeleteAsync(ct);
        }

        return Results.NoContent();
    }

    private static bool ValidEndpoint(string? endpoint) =>
        endpoint is { Length: > 0 and <= 1000 } && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool Key(string? value, int length, byte? first = null)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 200)
        {
            return false;
        }

        try
        {
            var bytes = WebPushCrypto.FromBase64Url(value);
            return bytes.Length == length && (first is null || bytes[0] == first);
        }
        catch (FormatException)
        {
            return false;
        }
    }

}
