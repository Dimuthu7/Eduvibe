using EduVibe.Modules.Materials.Domain;
using EduVibe.Modules.Materials.Drive;
using EduVibe.Shared.Plans;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduVibe.Modules.Materials.Endpoints;

/// <summary>Configured: the host has a Google client. Connected: this teacher linked their Drive. NeedsReconnect: Google stopped accepting the link.</summary>
public sealed record DriveStatusDto(bool Configured, bool Connected, string? Email, bool NeedsReconnect);

public sealed record ConsentDto(string Url);

/// <summary>Connecting and disconnecting a teacher's Google Drive.</summary>
internal static class DriveEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var drive = group.MapGroup("/drive").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.DriveMaterials);
        drive.MapGet("", Status);
        drive.MapPost("/connect", Connect);
        drive.MapDelete("", Disconnect);

        // Google sends the browser here. It carries no sign-in, so who is connecting comes from the signed state made in Connect.
        group.MapGet("/drive/callback", Callback).AllowAnonymous();
    }

    private static async Task<IResult> Status(MaterialsDbContext db, IDriveClient drive, CancellationToken ct)
    {
        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(ct);
        return Results.Ok(new DriveStatusDto(drive.IsConfigured, connection is not null, connection?.Email, connection?.NeedsReconnect ?? false));
    }

    private static IResult Connect(ITenantContext tenant, IDriveClient drive, DriveTokens tokens, TimeProvider clock)
    {
        if (!drive.IsConfigured)
        {
            return Problems.Reject("drive_not_configured", StatusCodes.Status409Conflict);
        }

        if (tenant.TenantId is not { } tenantId)
        {
            return Results.Forbid();
        }

        return Results.Ok(new ConsentDto(drive.ConsentUrl(tokens.SignState(tenantId, clock.GetUtcNow()))));
    }

    private static async Task<IResult> Disconnect(MaterialsDbContext db, IDriveClient drive, DriveTokens tokens, CancellationToken ct)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(ct);
        if (connection is null)
        {
            return Results.NoContent();
        }

        // The list of materials stays, so connecting the same Google account again brings everything back. Google is told to drop EduVibe's access.
        if (tokens.TryUnprotect(connection.RefreshToken) is { } token)
        {
            await drive.RevokeAsync(token, ct);
        }

        db.Connections.Remove(connection);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Callback(
        string? code, string? state, string? error, MaterialsDbContext db, IDriveClient drive, DriveTokens tokens, IPlanGate plans,
        IOptions<DriveOptions> options, IConfiguration configuration, TimeProvider clock, ILogger<DriveStatusDto> logger, CancellationToken ct)
    {
        var back = (string outcome) => Results.Redirect($"{WebAddress(options.Value, configuration)}/materials?drive={outcome}");

        if (tokens.ReadState(state, clock.GetUtcNow()) is not { } tenantId)
        {
            return back("failed");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return back("denied");
        }

        if (!(await plans.ForTeacherAsync(tenantId, ct)).Has(PlanFeatures.DriveMaterials))
        {
            return back("locked");
        }

        DriveGrant grant;
        try
        {
            grant = await drive.ExchangeCodeAsync(code, ct);
        }
        catch (DriveAuthException e)
        {
            logger.LogInformation("Drive sign-in refused: {Reason}", e.Message);
            return back("permission");
        }
        catch (DriveUnavailableException e)
        {
            logger.LogWarning(e, "Google could not be reached while connecting Drive");
            return back("failed");
        }

        var connection = await db.Connections.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.TenantId == tenantId, ct);
        var protectedToken = tokens.Protect(grant.RefreshToken);
        if (connection is null)
        {
            db.Connections.Add(new DriveConnection { TenantId = tenantId, Email = grant.Email, RefreshToken = protectedToken });
        }
        else
        {
            // Connecting again, perhaps with another Google account: the old folders belong to the old Drive.
            if (!string.Equals(connection.Email, grant.Email, StringComparison.OrdinalIgnoreCase))
            {
                connection.RootFolderId = null;
                db.ClassFolders.RemoveRange(await db.ClassFolders.IgnoreQueryFilters().Where(f => f.TenantId == tenantId).ToListAsync(ct));
            }

            connection.Email = grant.Email;
            connection.RefreshToken = protectedToken;
            connection.NeedsReconnect = false;
        }

        await db.SaveChangesAsync(ct);
        return back("connected");
    }

    /// <summary>The web app's address for the browser to return to.</summary>
    private static string WebAddress(DriveOptions options, IConfiguration configuration) =>
        (options.WebReturnUrl != "" ? options.WebReturnUrl : configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault() ?? "").TrimEnd('/');
}
