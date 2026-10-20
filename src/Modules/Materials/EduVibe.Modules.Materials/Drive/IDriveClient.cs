namespace EduVibe.Modules.Materials.Drive;

/// <summary>What Google gave back when a teacher agreed: a token to keep, and which account it is.</summary>
public sealed record DriveGrant(string RefreshToken, string? Email);

/// <summary>A file just stored in Drive.</summary>
public sealed record DriveFile(string Id, long SizeBytes);

/// <summary>A file being read from Drive. Dispose it to release the connection.</summary>
public sealed class DriveDownload(Stream content, string contentType, long? length, IDisposable? owner = null) : IDisposable
{
    public Stream Content { get; } = content;
    public string ContentType { get; } = contentType;
    public long? Length { get; } = length;

    public void Dispose()
    {
        Content.Dispose();
        owner?.Dispose();
    }
}

/// <summary>Google refused the teacher's token: they removed EduVibe's access or the grant expired. They must connect again.</summary>
public sealed class DriveAuthException(string message) : Exception(message);

/// <summary>Drive could not be reached or refused a request for some other reason.</summary>
public sealed class DriveUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Everything EduVibe does with Google Drive, kept behind one interface so the rest of the module (and its tests) never touch Google.
/// Every call that acts for a teacher takes that teacher's refresh token. Only the drive.file scope is used: EduVibe sees just the
/// folders and files it created.
/// </summary>
public interface IDriveClient
{
    /// <summary>False until the Google client id, secret and callback address are set.</summary>
    bool IsConfigured { get; }

    /// <summary>The Google page where the teacher agrees to let EduVibe use its own folder in their Drive.</summary>
    string ConsentUrl(string state);

    /// <summary>Turns the code Google sent to the callback into a refresh token. Throws <see cref="DriveAuthException"/> when the code is refused or the Drive permission was not given.</summary>
    Task<DriveGrant> ExchangeCodeAsync(string code, CancellationToken ct);

    /// <summary>Makes a folder and returns its id. A null parent means the top of the teacher's Drive.</summary>
    Task<string> CreateFolderAsync(string refreshToken, string name, string? parentId, CancellationToken ct);

    /// <summary>True when the folder is still there and not in the bin.</summary>
    Task<bool> FolderExistsAsync(string refreshToken, string folderId, CancellationToken ct);

    /// <summary>Stores a file in a folder, streaming it without holding it all in memory.</summary>
    Task<DriveFile> UploadAsync(string refreshToken, string folderId, string name, string contentType, Stream content, long length, CancellationToken ct);

    /// <summary>Opens a file for reading, or null when it is gone from Drive.</summary>
    Task<DriveDownload?> OpenAsync(string refreshToken, string fileId, CancellationToken ct);

    /// <summary>Removes a file from Drive. A file that is already gone is fine.</summary>
    Task DeleteAsync(string refreshToken, string fileId, CancellationToken ct);

    /// <summary>Tells Google to drop EduVibe's access. Best effort: it never throws.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken ct);
}
