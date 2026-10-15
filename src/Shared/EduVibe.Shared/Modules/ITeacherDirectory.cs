namespace EduVibe.Shared.Modules;

/// <summary>A teacher as the Super Admin sees them for a privacy request, active or not.</summary>
public sealed record TeacherIdentity(Guid Id, string FullName, bool IsActive);

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

    /// <summary>How many active teachers there are, across all of them.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>The teacher whatever their status, or null when there is no such teacher. For privacy requests, which reach switched-off teachers too.</summary>
    Task<TeacherIdentity?> FindIdentityAsync(Guid teacherId, CancellationToken cancellationToken = default);
}
