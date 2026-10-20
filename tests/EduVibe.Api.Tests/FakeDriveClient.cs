using EduVibe.Modules.Materials.Drive;

namespace EduVibe.Api.Tests;

/// <summary>A Google Drive that lives in memory. Tests can ask what it holds and make it fail the way Google does.</summary>
public sealed class FakeDriveClient : IDriveClient
{
    private readonly Lock _gate = new();
    private int _next;

    public bool IsConfigured { get; set; } = true;

    /// <summary>The account the next sign-in comes back as.</summary>
    public string Email { get; set; } = "teacher@example.com";

    /// <summary>When set, calls fail as if the teacher had removed EduVibe's access in their Google account.</summary>
    public bool RevokedByTeacher { get; set; }

    public bool Unreachable { get; set; }

    /// <summary>When set, calls fail with this error code, as when Google answers that the Drive API is off or the Drive is full.</summary>
    public string? FailWithCode { get; set; }

    public Dictionary<string, (string Name, string? Parent)> Folders { get; } = [];
    public Dictionary<string, (string Folder, string Name, string ContentType, byte[] Bytes)> Files { get; } = [];
    public List<string> RevokedTokens { get; } = [];
    public List<string> TokensUsed { get; } = [];

    public string ConsentUrl(string state) => $"https://accounts.example.test/consent?state={Uri.EscapeDataString(state)}";

    public Task<DriveGrant> ExchangeCodeAsync(string code, CancellationToken ct) =>
        code == "refused"
            ? throw new DriveAuthException("refused")
            : Task.FromResult(new DriveGrant("refresh-" + code, Email));

    private void Check(string token)
    {
        TokensUsed.Add(token);
        if (Unreachable)
        {
            throw new DriveUnavailableException("down");
        }

        if (FailWithCode is { } code)
        {
            throw new DriveUnavailableException("refused", code: code);
        }

        if (RevokedByTeacher)
        {
            throw new DriveAuthException("revoked");
        }
    }

    public Task<string> CreateFolderAsync(string refreshToken, string name, string? parentId, CancellationToken ct)
    {
        Check(refreshToken);
        lock (_gate)
        {
            var id = "folder-" + ++_next;
            Folders[id] = (name, parentId);
            return Task.FromResult(id);
        }
    }

    public Task<bool> FolderExistsAsync(string refreshToken, string folderId, CancellationToken ct)
    {
        Check(refreshToken);
        return Task.FromResult(Folders.ContainsKey(folderId));
    }

    public async Task<DriveFile> UploadAsync(string refreshToken, string folderId, string name, string contentType, Stream content, long length, CancellationToken ct)
    {
        Check(refreshToken);
        if (!Folders.ContainsKey(folderId))
        {
            throw new FolderMissingException();
        }

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, ct);
        lock (_gate)
        {
            var id = "file-" + ++_next;
            Files[id] = (folderId, name, contentType, memory.ToArray());
            return new DriveFile(id, memory.Length);
        }
    }

    public Task<DriveDownload?> OpenAsync(string refreshToken, string fileId, CancellationToken ct)
    {
        Check(refreshToken);
        return Task.FromResult(Files.TryGetValue(fileId, out var file)
            ? new DriveDownload(new MemoryStream(file.Bytes), file.ContentType, file.Bytes.Length)
            : null);
    }

    public Task DeleteAsync(string refreshToken, string fileId, CancellationToken ct)
    {
        Check(refreshToken);
        Files.Remove(fileId);
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        RevokedTokens.Add(refreshToken);
        return Task.CompletedTask;
    }
}
