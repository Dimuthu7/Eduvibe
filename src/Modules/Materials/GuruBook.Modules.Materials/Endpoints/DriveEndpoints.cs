using GuruBook.Modules.Materials.Domain;
using GuruBook.Modules.Materials.Drive;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.Materials.Endpoints;

/// <summary>Configured: the host has a Google client. Connected: this teacher linked their Drive. NeedsReconnect: Google stopped accepting the link.</summary>
public sealed record DriveStatusDto(bool Configured, bool Connected, string? Email, bool NeedsReconnect);

public sealed record ConsentDto(string Url);

/// <summary>What Google handed back, passed on by the web app: the one-time code and the state made in Connect.</summary>
public sealed record FinishConnectRequest(string? Code, string? State);

/// <summary>Connecting and disconnecting a teacher's Google Drive.</summary>
internal static class DriveEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var drive = group.MapGroup("/drive").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.DriveMaterials);
        drive.MapGet("", Status);
        drive.MapPost("/connect", Connect);
        drive.MapPost("/finish", Finish);
        drive.MapDelete("", Disconnect);

        // Google sends the browser here. It carries no sign-in, so it only checks the state and hands the code on to the web app,
        // which finishes while signed in.
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

        // The list of materials stays, so connecting the same Google account again brings everything back. Google is told to drop GuruBook's access.
        if (tokens.TryUnprotect(connection.RefreshToken) is { } token)
        {
            await drive.RevokeAsync(token, ct);
        }

        db.Connections.Remove(connection);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Google's return trip. The code is not used here: the browser carries it on to the web app (after the #, so it never reaches a
    /// server log), and the web app sends it to Finish with the teacher's sign-in.
    /// </summary>
    private static IResult Callback(string? code, string? state, string? error, DriveTokens tokens, IOptions<DriveOptions> options,
        IConfiguration configuration, TimeProvider clock)
    {
        var web = WebAddress(options.Value, configuration);
        if (tokens.ReadState(state, clock.GetUtcNow()) is null)
        {
            return Results.Redirect($"{web}/materials?drive=failed");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Results.Redirect($"{web}/materials?drive=denied");
        }

        return Results.Redirect($"{web}/materials?drive=finish#code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state!)}");
    }

    /// <summary>
    /// Links the Drive. Only the teacher who pressed Connect can finish: a Google link someone else started and sent to this teacher
    /// to open is refused, so nobody can tie their GuruBook to another teacher's Drive.
    /// </summary>
    private static async Task<IResult> Finish(
        FinishConnectRequest request, ITenantContext tenant, MaterialsDbContext db, IDriveClient drive, DriveTokens tokens,
        TimeProvider clock, ILogger<DriveStatusDto> logger, CancellationToken ct)
    {
        if (tokens.ReadState(request.State, clock.GetUtcNow()) is not { } tenantId || tenantId != tenant.TenantId || string.IsNullOrEmpty(request.Code))
        {
            return Problems.Reject("drive_link_expired");
        }

        DriveGrant grant;
        try
        {
            grant = await drive.ExchangeCodeAsync(request.Code, ct);
        }
        catch (DriveAuthException e)
        {
            logger.LogInformation("Drive sign-in refused: {Reason}", e.Message);
            return Problems.Reject("drive_permission", StatusCodes.Status409Conflict);
        }
        catch (DriveUnavailableException e)
        {
            logger.LogWarning(e, "Google could not be reached while connecting Drive");
            return Problems.Reject(DriveUnavailableException.Generic, StatusCodes.Status502BadGateway);
        }

        var connection = await db.Connections.SingleOrDefaultAsync(ct);
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
                db.ClassFolders.RemoveRange(await db.ClassFolders.ToListAsync(ct));
            }

            connection.Email = grant.Email;
            connection.RefreshToken = protectedToken;
            connection.NeedsReconnect = false;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new DriveStatusDto(drive.IsConfigured, true, grant.Email, false));
    }

    /// <summary>The web app's address for the browser to return to.</summary>
    private static string WebAddress(DriveOptions options, IConfiguration configuration) =>
        (options.WebReturnUrl != "" ? options.WebReturnUrl : configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault() ?? "").TrimEnd('/');
}
