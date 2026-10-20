using EduVibe.Modules.Materials.Domain;
using EduVibe.Modules.Materials.Drive;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Plans;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduVibe.Modules.Materials.Endpoints;

public sealed record UpdateMaterialRequest(string? Title, string? Kind, string? Topic, int? Year);

/// <summary>A teacher's notes and past papers: add a file to a class (it goes to the teacher's Drive), list, edit, remove and open them.</summary>
internal static class MaterialEndpoints
{
    private const string RootFolderName = "EduVibe";

    public static void Map(RouteGroupBuilder group)
    {
        var materials = group.MapGroup("").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.DriveMaterials);
        materials.MapGet("", List);
        materials.MapPost("", Add).DisableAntiforgery();
        materials.MapPut("/{id:guid}", Update);
        materials.MapDelete("/{id:guid}", Remove);
        materials.MapGet("/{id:guid}/file", Open);
    }

    private static async Task<IResult> List(Guid? classId, string? kind, string? topic, int? year, MaterialsDbContext db, CancellationToken ct)
    {
        var query = db.Materials.AsNoTracking().AsQueryable();
        if (classId is { } cid)
        {
            query = query.Where(m => m.ClassId == cid);
        }

        if (Enum.TryParse<MaterialKind>(kind, ignoreCase: true, out var parsed))
        {
            query = query.Where(m => m.Kind == parsed);
        }

        if (Problems.Clean(topic) is { } t)
        {
            query = query.Where(m => m.Topic == t);
        }

        if (year is { } y)
        {
            query = query.Where(m => m.Year == y);
        }

        // Ordered here, not in SQL, because the tests' SQLite cannot sort a DateTimeOffset.
        var items = (await query.ToListAsync(ct)).OrderByDescending(m => m.Year).ThenByDescending(m => m.CreatedAt);
        return Results.Ok(items.Select(MaterialFiles.ToDto));
    }

    private static async Task<IResult> Add(
        HttpRequest request, MaterialsDbContext db, IClassDirectory classes, IDriveClient drive, DriveTokens tokens, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Problems.Reject("material_invalid");
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Problems.Reject("material_file_required");
        }

        if (file.Length > MaterialFiles.MaxBytes)
        {
            return Problems.Reject("material_too_large", StatusCodes.Status413PayloadTooLarge);
        }

        var fileName = MaterialFiles.CleanName(file.FileName);
        if (fileName == "" || MaterialFiles.TypeOf(fileName) is not { } contentType)
        {
            return Problems.Reject("material_type_not_allowed", StatusCodes.Status415UnsupportedMediaType);
        }

        if (!Guid.TryParse(form["classId"], out var classId) || await classes.FindAsync(classId, ct) is not { } cls)
        {
            return Problems.Reject("material_class_required");
        }

        var title = Problems.Clean(form["title"]) ?? Path.GetFileNameWithoutExtension(fileName);
        var topic = Problems.Clean(form["topic"]);
        int? year = int.TryParse(form["year"], out var y) ? y : null;
        if (title.Length > 150 || topic?.Length > 80 || year is < 1990 or > 2100 || !Enum.TryParse<MaterialKind>(form["kind"], ignoreCase: true, out var kind))
        {
            return Problems.Reject("material_invalid");
        }

        var connection = await db.Connections.SingleOrDefaultAsync(ct);
        if (connection is null)
        {
            return Problems.Reject("drive_not_connected", StatusCodes.Status409Conflict);
        }

        if (connection.NeedsReconnect || tokens.TryUnprotect(connection.RefreshToken) is not { } token)
        {
            return Problems.Reject("drive_reconnect", StatusCodes.Status409Conflict);
        }

        DriveFile stored;
        try
        {
            stored = await Upload(db, connection, drive, token, cls, fileName, contentType, file, ct);
        }
        catch (DriveAuthException)
        {
            connection.NeedsReconnect = true;
            await db.SaveChangesAsync(ct);
            return Problems.Reject("drive_reconnect", StatusCodes.Status409Conflict);
        }
        catch (DriveUnavailableException)
        {
            return Problems.Reject("drive_unavailable", StatusCodes.Status502BadGateway);
        }

        var material = new Material
        {
            ClassId = classId, DriveFileId = stored.Id, Title = title, FileName = fileName, ContentType = contentType,
            SizeBytes = stored.SizeBytes, Kind = kind, Topic = topic, Year = year,
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/materials/{material.Id}", MaterialFiles.ToDto(material));
    }

    /// <summary>Puts the file in the class's Drive folder, making the EduVibe folder and the class folder first if needed. A folder the teacher deleted is made again once.</summary>
    private static async Task<DriveFile> Upload(
        MaterialsDbContext db, DriveConnection connection, IDriveClient drive, string token, ClassSummary cls, string fileName, string contentType,
        IFormFile file, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var folderId = await ClassFolderAsync(db, connection, drive, token, cls, ct);
            try
            {
                await using var content = file.OpenReadStream();
                return await drive.UploadAsync(token, folderId, fileName, contentType, content, file.Length, ct);
            }
            catch (FolderMissingException) when (attempt == 0)
            {
                // The teacher removed the folder in Drive: forget it and make it again.
                connection.RootFolderId = null;
                db.ClassFolders.RemoveRange(await db.ClassFolders.ToListAsync(ct));
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task<string> ClassFolderAsync(
        MaterialsDbContext db, DriveConnection connection, IDriveClient drive, string token, ClassSummary cls, CancellationToken ct)
    {
        var existing = await db.ClassFolders.SingleOrDefaultAsync(f => f.ClassId == cls.Id, ct);
        if (existing is not null)
        {
            return existing.DriveFolderId;
        }

        if (connection.RootFolderId is null)
        {
            connection.RootFolderId = await drive.CreateFolderAsync(token, RootFolderName, null, ct);
        }

        var folderId = await drive.CreateFolderAsync(token, MaterialFiles.CleanName(cls.Title) is { Length: > 0 } name ? name : "Class", connection.RootFolderId, ct);
        db.ClassFolders.Add(new ClassFolder { ClassId = cls.Id, DriveFolderId = folderId });
        await db.SaveChangesAsync(ct);
        return folderId;
    }

    private static async Task<IResult> Update(Guid id, UpdateMaterialRequest request, MaterialsDbContext db, CancellationToken ct)
    {
        var material = await db.Materials.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Results.NotFound();
        }

        var title = Problems.Clean(request.Title);
        var topic = Problems.Clean(request.Topic);
        if (title is null || title.Length > 150 || topic?.Length > 80 || request.Year is < 1990 or > 2100
            || !Enum.TryParse<MaterialKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return Problems.Reject("material_invalid");
        }

        material.Title = title;
        material.Kind = kind;
        material.Topic = topic;
        material.Year = request.Year;
        await db.SaveChangesAsync(ct);
        return Results.Ok(MaterialFiles.ToDto(material));
    }

    private static async Task<IResult> Remove(Guid id, MaterialsDbContext db, IDriveClient drive, DriveTokens tokens, ILogger<MaterialsModule> logger, CancellationToken ct)
    {
        var material = await db.Materials.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Results.NoContent();
        }

        // The file is removed from Drive when that works. Whatever happens there, it is gone from the class so students stop seeing it.
        var connection = await db.Connections.SingleOrDefaultAsync(ct);
        if (connection is not null && tokens.TryUnprotect(connection.RefreshToken) is { } token)
        {
            try
            {
                await drive.DeleteAsync(token, material.DriveFileId, ct);
            }
            catch (Exception e) when (e is DriveAuthException or DriveUnavailableException)
            {
                logger.LogWarning(e, "Could not remove material {MaterialId} from Drive", id);
            }
        }

        db.Materials.Remove(material);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Open(Guid id, bool? download, MaterialsDbContext db, IDriveClient drive, DriveTokens tokens, HttpResponse response, CancellationToken ct)
    {
        var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Results.NotFound();
        }

        var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(ct);
        return await MaterialFiles.StreamAsync(material, connection, tokens, drive, db, response, download ?? false, ct);
    }
}
