using EduVibe.Shared.Auditing;
using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;

namespace EduVibe.Modules.Materials.Domain;

/// <summary>
/// A teacher's link to their own Google Drive. EduVibe keeps only a refresh token (encrypted) so it can add and read files the app
/// itself created there (the drive.file scope); the files stay in the teacher's Drive. One per teacher.
/// </summary>
public sealed class DriveConnection : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }

    /// <summary>The Google account that was connected, shown so the teacher can tell which Drive it is.</summary>
    public string? Email { get; set; }

    /// <summary>The refresh token, encrypted. Never sent to the web app and never part of an export.</summary>
    [NotAudited]
    public required string RefreshToken { get; set; }

    /// <summary>The "EduVibe" folder in the teacher's Drive that holds a subfolder per class.</summary>
    public string? RootFolderId { get; set; }

    /// <summary>Set when Google refused the token (the teacher removed EduVibe's access); the teacher must connect again.</summary>
    public bool NeedsReconnect { get; set; }
}

/// <summary>The Drive folder of one class, inside the teacher's EduVibe folder. ClassId belongs to the Classes module and is kept as a plain id.</summary>
public sealed class ClassFolder : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }
    public required string DriveFolderId { get; set; }
}

public enum MaterialKind
{
    Notes,
    PastPaper,
    Other,
}

/// <summary>What EduVibe knows about a file in the teacher's Drive: where it is and how to list it. The bytes stay in Drive.</summary>
public sealed class Material : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }
    public required string DriveFileId { get; set; }
    public required string Title { get; set; }

    /// <summary>The file's name in Drive, with its extension.</summary>
    public required string FileName { get; set; }

    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public MaterialKind Kind { get; set; }
    public string? Topic { get; set; }

    /// <summary>The year of the paper or notes (for example the exam year), or null.</summary>
    public int? Year { get; set; }
}
