using EduVibe.Modules.Materials.Domain;
using EduVibe.Modules.Materials.Drive;
using EduVibe.Shared.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduVibe.Modules.Materials.Endpoints;

/// <summary>A teacher's folder as the web app sees it. ParentId is null for a folder directly on its Notes, Past papers or Other shelf.</summary>
public sealed record MaterialFolderDto(Guid Id, Guid ClassId, string Kind, Guid? ParentId, string Name);

public sealed record CreateFolderRequest(Guid ClassId, string? Kind, Guid? ParentId, string? Name);

public sealed record RenameFolderRequest(string? Name);

/// <summary>
/// Folders inside a class's Notes, Past papers and Other. They are real folders in the teacher's Drive too, so the teacher finds the same
/// structure there. Folders hold files and other folders, up to <see cref="MaxDepth"/> levels.
/// </summary>
internal static class FolderEndpoints
{
    public const int MaxDepth = 4;
    private const int MaxName = 60;
    private const int MaxPerShelf = 100;

    public static void Map(RouteGroupBuilder materials)
    {
        var folders = materials.MapGroup("/folders");
        folders.MapGet("", List);
        folders.MapPost("", Create);
        folders.MapPut("/{id:guid}", Rename);
        folders.MapDelete("/{id:guid}", Remove);
    }

    private static MaterialFolderDto ToDto(MaterialFolder f, IReadOnlyCollection<MaterialFolder> all) =>
        new(f.Id, f.ClassId, f.Kind.ToString(), all.Any(p => p.Id == f.ParentId && p.IsKindRoot) ? null : f.ParentId, f.Name);

    private static async Task<IResult> List(Guid? classId, MaterialsDbContext db, CancellationToken ct)
    {
        var query = db.Folders.AsNoTracking().AsQueryable();
        if (classId is { } cid)
        {
            query = query.Where(f => f.ClassId == cid);
        }

        var all = await query.ToListAsync(ct);
        return Results.Ok(all.Where(f => !f.IsKindRoot).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Select(f => ToDto(f, all)));
    }

    private static async Task<IResult> Create(
        CreateFolderRequest request, MaterialsDbContext db, IClassDirectory classes, IDriveClient drive, DriveTokens tokens, CancellationToken ct)
    {
        if (await classes.FindAsync(request.ClassId, ct) is not { } cls)
        {
            return Problems.Reject("material_class_required");
        }

        var name = CleanFolderName(request.Name);
        if (name is null || !Enum.TryParse<MaterialKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return Problems.Reject("folder_invalid");
        }

        var shelf = await db.Folders.Where(f => f.ClassId == cls.Id && f.Kind == kind).ToListAsync(ct);
        var parent = request.ParentId is { } parentId ? shelf.SingleOrDefault(f => f.Id == parentId && !f.IsKindRoot) : null;
        if (request.ParentId is not null && parent is null)
        {
            return Problems.Reject("folder_invalid");
        }

        if (Depth(parent, shelf) >= MaxDepth)
        {
            return Problems.Reject("folder_too_deep");
        }

        if (shelf.Count(f => !f.IsKindRoot) >= MaxPerShelf)
        {
            return Problems.Reject("folder_limit");
        }

        if (NameTaken(shelf, parent?.Id, name, except: null))
        {
            return Problems.Reject("folder_name_taken", StatusCodes.Status409Conflict);
        }

        var (link, problem) = await DriveFolders.LinkAsync(db, tokens, ct);
        if (problem is not null)
        {
            return problem;
        }

        string? driveId = null;
        problem = await DriveFolders.GuardAsync(link!, db, async () =>
        {
            driveId = await DriveFolders.WithRepairAsync(async verify =>
            {
                var place = await DriveFolders.PlaceAsync(db, link!.Connection, drive, link.Token, cls, kind, parent, verify, ct);
                return await drive.CreateFolderAsync(link.Token, name, place, ct);
            });
        }, ct);
        if (problem is not null)
        {
            return problem;
        }

        // The shelf row is made on the first need, so it is read again now that the Drive folders are in place.
        var shelfRow = await db.Folders.SingleAsync(f => f.ClassId == cls.Id && f.Kind == kind && f.IsKindRoot, ct);
        var folder = new MaterialFolder { ClassId = cls.Id, Kind = kind, ParentId = parent?.Id ?? shelfRow.Id, Name = name, DriveFolderId = driveId! };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/materials/folders/{folder.Id}", ToDto(folder, [shelfRow]));
    }

    private static async Task<IResult> Rename(
        Guid id, RenameFolderRequest request, MaterialsDbContext db, IDriveClient drive, DriveTokens tokens, ILogger<MaterialsModule> logger, CancellationToken ct)
    {
        var folder = await db.Folders.SingleOrDefaultAsync(f => f.Id == id && !f.IsKindRoot, ct);
        if (folder is null)
        {
            return Results.NotFound();
        }

        var name = CleanFolderName(request.Name);
        if (name is null)
        {
            return Problems.Reject("folder_invalid");
        }

        var all = await db.Folders.Where(f => f.ClassId == folder.ClassId && f.Kind == folder.Kind).ToListAsync(ct);
        if (NameTaken(all, folder.ParentId, name, except: folder.Id))
        {
            return Problems.Reject("folder_name_taken", StatusCodes.Status409Conflict);
        }

        folder.Name = name;
        await db.SaveChangesAsync(ct);

        // The name in EduVibe is what counts; Drive follows when it can.
        var (link, problem) = await DriveFolders.LinkAsync(db, tokens, ct);
        if (problem is null)
        {
            var failure = await DriveFolders.GuardAsync(link!, db, () => drive.RenameAsync(link!.Token, folder.DriveFolderId, name, ct), ct);
            if (failure is not null)
            {
                logger.LogWarning("Could not rename folder {FolderId} in Drive", id);
            }
        }

        return Results.Ok(ToDto(folder, all));
    }

    private static async Task<IResult> Remove(Guid id, MaterialsDbContext db, IDriveClient drive, DriveTokens tokens, ILogger<MaterialsModule> logger, CancellationToken ct)
    {
        var folder = await db.Folders.SingleOrDefaultAsync(f => f.Id == id && !f.IsKindRoot, ct);
        if (folder is null)
        {
            return Results.NoContent();
        }

        // Only an empty folder goes, so removing one can never lose files by accident.
        if (await db.Folders.AnyAsync(f => f.ParentId == id, ct) || await db.Materials.AnyAsync(m => m.FolderId == id, ct))
        {
            return Problems.Reject("folder_not_empty", StatusCodes.Status409Conflict);
        }

        var (link, problem) = await DriveFolders.LinkAsync(db, tokens, ct);
        if (problem is null)
        {
            var failure = await DriveFolders.GuardAsync(link!, db, () => drive.DeleteAsync(link!.Token, folder.DriveFolderId, ct), ct);
            if (failure is not null)
            {
                logger.LogWarning("Could not remove folder {FolderId} from Drive", id);
            }
        }

        db.Folders.Remove(folder);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static string? CleanFolderName(string? name)
    {
        var cleaned = MaterialFiles.CleanName(name ?? string.Empty);
        return cleaned.Length is > 0 and <= MaxName ? cleaned : null;
    }

    private static bool NameTaken(IEnumerable<MaterialFolder> all, Guid? parentId, string name, Guid? except)
    {
        var list = all as IReadOnlyCollection<MaterialFolder> ?? [.. all];
        var shelfId = list.SingleOrDefault(f => f.IsKindRoot)?.Id;
        var under = parentId ?? shelfId;
        return list.Any(f => !f.IsKindRoot && f.Id != except && f.ParentId == under && string.Equals(f.Name, name, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>How many folders deep this one is (a folder directly on a shelf is 1; no folder is 0).</summary>
    private static int Depth(MaterialFolder? folder, IReadOnlyCollection<MaterialFolder> all)
    {
        var depth = 0;
        for (var step = folder; step is { IsKindRoot: false }; step = all.SingleOrDefault(f => f.Id == step.ParentId))
        {
            depth++;
        }

        return depth;
    }
}
