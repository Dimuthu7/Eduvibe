namespace EduVibe.Shared.Modules;

/// <summary>
/// Lets other modules check teaching streams and subjects without reading the Catalog module's tables.
/// Catalog implements it; Identity uses it to validate a teacher's profile.
/// </summary>
public interface ICatalogDirectory
{
    Task<bool> StreamIsActiveAsync(Guid streamId, CancellationToken cancellationToken = default);

    /// <summary>Returns the ids from <paramref name="subjectIds"/> that are active subjects.</summary>
    Task<IReadOnlySet<Guid>> FindActiveSubjectsAsync(IEnumerable<Guid> subjectIds, CancellationToken cancellationToken = default);
}
