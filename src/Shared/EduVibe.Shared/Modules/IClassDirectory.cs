namespace EduVibe.Shared.Modules;

/// <summary>What other modules may know about a class: enough to enrol into it and to show its name.</summary>
public sealed record ClassSummary(Guid Id, Guid TenantId, string Title, decimal MonthlyFee, bool IsArchived);

/// <summary>
/// Lets other modules ask about classes without reading the Classes module's tables.
/// Classes implements it; Students and later modules use it.
/// </summary>
public interface IClassDirectory
{
    /// <summary>A class of the current teacher, or null when there is none with that id.</summary>
    Task<ClassSummary?> FindAsync(Guid classId, CancellationToken cancellationToken = default);

    /// <summary>The current teacher's classes among <paramref name="classIds"/>, by id.</summary>
    Task<IReadOnlyDictionary<Guid, ClassSummary>> FindManyAsync(IEnumerable<Guid> classIds, CancellationToken cancellationToken = default);

    /// <summary>A class of any teacher. Only for anonymous pages such as the self-registration link, which carry no teacher.</summary>
    Task<ClassSummary?> FindInAnyTenantAsync(Guid classId, CancellationToken cancellationToken = default);
}
