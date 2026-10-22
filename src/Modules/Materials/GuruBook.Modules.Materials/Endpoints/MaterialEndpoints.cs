using GuruBook.Modules.Materials.Domain;
using GuruBook.Modules.Materials.Drive;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Materials.Endpoints;

public sealed record UpdateMaterialRequest(string? Title, string? Kind, string? Topic, int? Year, Guid? FolderId);

/// <summary>A teacher's notes and past papers: add a file to a class (it goes to the teacher's Drive), list, edit, remove and open them.</summary>
internal static class MaterialEndpoints
{
    private const int MaxTitle = 120;

    public static void Map(RouteGroupBuilder group)
    {
        var materials = group.MapGroup("").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.DriveMaterials);
        materials.MapGet("", List);
        materials.MapPost("", Add).DisableAntiforgery();
        materials.MapPut("/{id:guid}", Update);
        materials.MapDelete("/{id:guid}", Remove);
        materials.MapGet("/{id:guid}/file", Open);
        FolderEndpoints.Map(materials);
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

        var title = Problems.Clean(form["title"]) ?? Truncate(Path.GetFileNameWithoutExtension(fileName), MaxTitle);
        var topic = Problems.Clean(form["topic"]);
        int? year = int.TryParse(form["year"], out var y) ? y : null;
        if (title.Length > MaxTitle || topic?.Length > 80 || year is < 1990 or > 2100 || !Enum.TryParse<MaterialKind>(form["kind"], ignoreCase: true, out var kind))
        {
            return Problems.Reject("material_invalid");
        }

        var folder = await FolderFor(db, form["folderId"], classId, kind, ct);
        if (folder.Invalid)
        {
            return Problems.Reject("material_folder_invalid");
        }

        var (link, problem) = await DriveFolders.LinkAsync(db, tokens, ct);
        if (problem is not null)
        {
            return problem;
        }

        DriveFile? stored = null;
        string? parentId = null;
        problem = await DriveFolders.GuardAsync(link!, db, async () =>
        {
            (stored, parentId) = await DriveFolders.WithRepairAsync(async verify =>
            {
                var place = await DriveFolders.PlaceAsync(db, link!.Connection, drive, link.Token, cls, kind, folder.Row, verify, ct);
                await using var content = file.OpenReadStream();
                return (await drive.UploadAsync(link.Token, place, fileName, contentType, content, file.Length, ct), place);
            });
        }, ct);
        if (problem is not null)
        {
            return problem;
        }

        var material = new Material
        {
            ClassId = classId, FolderId = folder.Row?.Id, DriveFileId = stored!.Id, DriveParentId = parentId, Title = title, FileName = fileName, ContentType = contentType,
            SizeBytes = stored.SizeBytes, Kind = kind, Topic = topic, Year = year,
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/materials/{material.Id}", MaterialFiles.ToDto(material));
    }

    /// <summary>The folder a request names, which must be one of the teacher's own folders of that class and shelf. No folder means the shelf itself.</summary>
    private static async Task<(MaterialFolder? Row, bool Invalid)> FolderFor(MaterialsDbContext db, string? text, Guid classId, MaterialKind kind, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, false);
        }

        if (!Guid.TryParse(text, out var id))
        {
            return (null, true);
        }

        var row = await db.Folders.SingleOrDefaultAsync(f => f.Id == id && f.ClassId == classId && f.Kind == kind && !f.IsKindRoot, ct);
        return (row, row is null);
    }

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;

    private static async Task<IResult> Update(
        Guid id, UpdateMaterialRequest request, MaterialsDbContext db, IClassDirectory classes, IDriveClient drive, DriveTokens tokens, ILogger<MaterialsModule> logger, CancellationToken ct)
    {
        var material = await db.Materials.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (material is null)
        {
            return Results.NotFound();
        }

        var title = Problems.Clean(request.Title);
        var topic = Problems.Clean(request.Topic);
        if (title is null || title.Length > MaxTitle || topic?.Length > 80 || request.Year is < 1990 or > 2100
            || !Enum.TryParse<MaterialKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return Problems.Reject("material_invalid");
        }

        var folder = await FolderFor(db, request.FolderId?.ToString(), material.ClassId, kind, ct);
        if (folder.Invalid)
        {
            return Problems.Reject("material_folder_invalid");
        }

        var moved = material.Kind != kind || material.FolderId != folder.Row?.Id;
        material.Title = title;
        material.Kind = kind;
        material.FolderId = folder.Row?.Id;
        material.Topic = topic;
        material.Year = request.Year;
        await db.SaveChangesAsync(ct);
        if (moved)
        {
            await MoveInDriveAsync(material, folder.Row, db, classes, drive, tokens, logger, ct);
        }

        return Results.Ok(MaterialFiles.ToDto(material));
    }

    /// <summary>
    /// Puts the file in the matching Drive folder. The list in GuruBook is what students see, so a Drive that cannot be reached only leaves
    /// the file where it was in Drive: it is not an error for the teacher, and the next move starts from where the file really is.
    /// </summary>
    private static async Task MoveInDriveAsync(
        Material material, MaterialFolder? folder, MaterialsDbContext db, IClassDirectory classes, IDriveClient drive, DriveTokens tokens, ILogger<MaterialsModule> logger, CancellationToken ct)
    {
        var (link, problem) = await DriveFolders.LinkAsync(db, tokens, ct);
        if (problem is not null || await classes.FindAsync(material.ClassId, ct) is not { } cls)
        {
            return;
        }

        var from = await DriveFolders.CurrentParentAsync(db, material, ct);
        if (from is null)
        {
            return;
        }

        string? target = null;
        var failure = await DriveFolders.GuardAsync(link!, db, async () =>
        {
            target = await DriveFolders.WithRepairAsync(async verify =>
            {
                var place = await DriveFolders.PlaceAsync(db, link!.Connection, drive, link.Token, cls, material.Kind, folder, verify, ct);
                if (place != from)
                {
                    await drive.MoveAsync(link.Token, material.DriveFileId, from, place, ct);
                }

                return place;
            });
        }, ct);
        if (failure is null)
        {
            material.DriveParentId = target;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            logger.LogWarning("Could not move material {MaterialId} in Drive", material.Id);
        }
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
