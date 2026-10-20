using EduVibe.Modules.Materials.Domain;
using EduVibe.Modules.Materials.Drive;
using EduVibe.Shared.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Materials.Endpoints;

/// <summary>
/// Where things live in a teacher's Drive: EduVibe / class / Notes, Past papers or Other / the teacher's own folders. Each level is a row
/// here that remembers its Drive folder id, so a folder is made once, on the first need, and made again if the teacher deleted it in Drive.
/// </summary>
internal static class DriveFolders
{
    private const string RootName = "EduVibe";

    public static string ShelfName(MaterialKind kind) => kind switch
    {
        MaterialKind.Notes => "Notes",
        MaterialKind.PastPaper => "Past papers",
        _ => "Other",
    };

    /// <summary>
    /// The Drive folder for a place (a teacher's folder, or the Notes, Past papers or Other shelf when folder is null), made on the way if missing.
    /// With <paramref name="verify"/> every folder on the way is checked in Drive and made again when it is gone or in the bin.
    /// </summary>
    public static async Task<string> PlaceAsync(
        MaterialsDbContext db, DriveConnection connection, IDriveClient drive, string token, ClassSummary cls, MaterialKind kind, MaterialFolder? folder, bool verify, CancellationToken ct)
    {
        var rootId = await EnsureAsync(connection.RootFolderId, RootName, null, verify, drive, token, ct);
        connection.RootFolderId = rootId;

        var classRow = await db.ClassFolders.SingleOrDefaultAsync(f => f.ClassId == cls.Id, ct);
        var className = MaterialFiles.CleanName(cls.Title) is { Length: > 0 } cleaned ? cleaned : "Class";
        var classId = await EnsureAsync(classRow?.DriveFolderId, className, rootId, verify, drive, token, ct);
        if (classRow is null)
        {
            db.ClassFolders.Add(new ClassFolder { ClassId = cls.Id, DriveFolderId = classId });
        }
        else
        {
            classRow.DriveFolderId = classId;
        }

        var rows = await db.Folders.Where(f => f.ClassId == cls.Id && f.Kind == kind).ToListAsync(ct);
        var shelf = rows.SingleOrDefault(f => f.IsKindRoot);
        var shelfId = await EnsureAsync(shelf?.DriveFolderId, ShelfName(kind), classId, verify, drive, token, ct);
        if (shelf is null)
        {
            shelf = new MaterialFolder { ClassId = cls.Id, Kind = kind, IsKindRoot = true, Name = ShelfName(kind), DriveFolderId = shelfId };
            db.Folders.Add(shelf);
        }
        else
        {
            shelf.DriveFolderId = shelfId;
        }

        var parentId = shelfId;
        foreach (var step in ChainOf(folder, rows))
        {
            parentId = await EnsureAsync(step.DriveFolderId, step.Name, parentId, verify, drive, token, ct);
            step.DriveFolderId = parentId;
        }

        await db.SaveChangesAsync(ct);
        return parentId;
    }

    /// <summary>The folders from the shelf down to <paramref name="folder"/>, top first. The rows are tracked, so their Drive ids can be updated in place.</summary>
    private static List<MaterialFolder> ChainOf(MaterialFolder? folder, List<MaterialFolder> rows)
    {
        var chain = new List<MaterialFolder>();
        for (var step = folder; step is { IsKindRoot: false }; step = rows.SingleOrDefault(f => f.Id == step.ParentId))
        {
            chain.Insert(0, rows.Single(f => f.Id == step.Id));
        }

        return chain;
    }

    private static async Task<string> EnsureAsync(string? existing, string name, string? parentId, bool verify, IDriveClient drive, string token, CancellationToken ct)
    {
        if (existing is not null && (!verify || await drive.FolderExistsAsync(token, existing, ct)))
        {
            return existing;
        }

        return await drive.CreateFolderAsync(token, name, parentId, ct);
    }

    /// <summary>Runs a Drive call, trying again once with every folder checked if one turns out to be missing.</summary>
    public static async Task<T> WithRepairAsync<T>(Func<bool, Task<T>> call)
    {
        try
        {
            return await call(false);
        }
        catch (FolderMissingException)
        {
            return await call(true);
        }
    }

    /// <summary>The Drive folder a material sits in now.</summary>
    public static async Task<string?> CurrentParentAsync(MaterialsDbContext db, Material material, CancellationToken ct) =>
        material.DriveParentId ?? (await db.ClassFolders.SingleOrDefaultAsync(f => f.ClassId == material.ClassId, ct))?.DriveFolderId;

    /// <summary>The teacher's usable Drive connection, or the answer that tells them to connect first.</summary>
    public static async Task<(DriveLink? Link, IResult? Problem)> LinkAsync(MaterialsDbContext db, DriveTokens tokens, CancellationToken ct)
    {
        var connection = await db.Connections.SingleOrDefaultAsync(ct);
        if (connection is null)
        {
            return (null, Problems.Reject("drive_not_connected", StatusCodes.Status409Conflict));
        }

        if (connection.NeedsReconnect || tokens.TryUnprotect(connection.RefreshToken) is not { } token)
        {
            return (null, Problems.Reject("drive_reconnect", StatusCodes.Status409Conflict));
        }

        return (new DriveLink(connection, token), null);
    }

    /// <summary>Runs a Drive action for the teacher. When Google refuses the connection or cannot be reached, returns the answer to send instead.</summary>
    public static async Task<IResult?> GuardAsync(DriveLink link, MaterialsDbContext db, Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
            return null;
        }
        catch (DriveAuthException)
        {
            link.Connection.NeedsReconnect = true;
            await db.SaveChangesAsync(ct);
            return Problems.Reject("drive_reconnect", StatusCodes.Status409Conflict);
        }
        catch (DriveUnavailableException e)
        {
            return Problems.Reject(e.Code, StatusCodes.Status502BadGateway);
        }
        catch (FolderMissingException)
        {
            // A folder was still missing after the repair attempt, for example deleted again while the request ran.
            return Problems.Reject(DriveUnavailableException.Generic, StatusCodes.Status502BadGateway);
        }
    }
}

internal sealed record DriveLink(DriveConnection Connection, string Token);
