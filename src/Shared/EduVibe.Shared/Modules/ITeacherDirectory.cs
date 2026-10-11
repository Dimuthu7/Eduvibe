namespace EduVibe.Shared.Modules;

/// <summary>
/// Lets other modules ask whether teachers exist without reading the Identity module's tables.
/// Identity implements it; Classes and later modules use it.
/// </summary>
public interface ITeacherDirectory
{
    /// <summary>Returns the ids from <paramref name="teacherIds"/> that belong to an active teacher.</summary>
    Task<IReadOnlySet<Guid>> FindActiveAsync(IEnumerable<Guid> teacherIds, CancellationToken cancellationToken = default);

    /// <summary>The teacher's full name, or null when there is no such teacher.</summary>
    Task<string?> FindNameAsync(Guid teacherId, CancellationToken cancellationToken = default);
}
