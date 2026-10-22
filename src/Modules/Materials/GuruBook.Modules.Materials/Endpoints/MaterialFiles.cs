using GuruBook.Modules.Materials.Domain;
using GuruBook.Modules.Materials.Drive;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Materials.Endpoints;

/// <summary>What the web app sees of one material. Student lists add the class and teacher names through <see cref="StudentMaterialDto"/>.</summary>
public sealed record MaterialDto(
    Guid Id, Guid ClassId, Guid? FolderId, string Title, string FileName, string ContentType, long SizeBytes, string Kind, string? Topic, int? Year, DateTimeOffset AddedAt, bool CanPreview);

/// <summary>The file types a teacher may add: what a phone can open. The type is set from the extension, never from what the browser claims.</summary>
internal static class MaterialFiles
{
    public const long MaxBytes = 25L * 1024 * 1024;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    public static string? TypeOf(string fileName) => Types.GetValueOrDefault(Path.GetExtension(fileName));

    /// <summary>Types a browser can show in place; the rest are always downloaded.</summary>
    public static bool CanPreview(string contentType) =>
        contentType is "application/pdf" or "text/plain" || contentType.StartsWith("image/", StringComparison.Ordinal);

    /// <summary>A file name that is safe to store in Drive and to send in a header.</summary>
    public static string CleanName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var cleaned = new string([.. name.Where(c => !char.IsControl(c) && c is not ('"' or '/' or '\\' or ':' or '*' or '?' or '<' or '>' or '|'))]).Trim();
        return cleaned.Length > 150 ? cleaned[^150..] : cleaned;
    }

    public static MaterialDto ToDto(Material m) =>
        new(m.Id, m.ClassId, m.FolderId, m.Title, m.FileName, m.ContentType, m.SizeBytes, m.Kind.ToString(), m.Topic, m.Year, m.CreatedAt, CanPreview(m.ContentType));

    /// <summary>
    /// Sends a material's bytes from Drive to the browser without keeping them. A file that was deleted in Drive answers 404 and a revoked
    /// connection answers 409 so the teacher can connect again.
    /// </summary>
    public static async Task<IResult> StreamAsync(
        Material material, DriveConnection? connection, DriveTokens tokens, IDriveClient drive, MaterialsDbContext db, HttpResponse response, bool download, CancellationToken ct)
    {
        if (connection is null || tokens.TryUnprotect(connection.RefreshToken) is not { } token)
        {
            return Problems.Reject("drive_not_connected", StatusCodes.Status409Conflict);
        }

        DriveDownload? file;
        try
        {
            file = await drive.OpenAsync(token, material.DriveFileId, ct);
        }
        catch (DriveAuthException)
        {
            await MarkNeedsReconnectAsync(connection.TenantId, db, ct);
            return Problems.Reject("drive_reconnect", StatusCodes.Status409Conflict);
        }
        catch (DriveUnavailableException e)
        {
            return Problems.Reject(e.Code, StatusCodes.Status502BadGateway);
        }

        if (file is null)
        {
            return Problems.Reject("material_file_missing", StatusCodes.Status404NotFound);
        }

        response.RegisterForDispose(file);
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "private, no-store";
        var inline = !download && CanPreview(material.ContentType);
        return Results.File(file.Content, material.ContentType, inline ? null : material.FileName, enableRangeProcessing: false);
    }

    public static Task MarkNeedsReconnectAsync(Guid tenantId, MaterialsDbContext db, CancellationToken ct) =>
        db.Connections.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ExecuteUpdateAsync(s => s.SetProperty(c => c.NeedsReconnect, true), ct);
}
