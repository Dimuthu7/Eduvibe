namespace EduVibe.Shared.Modules;

/// <summary>
/// One number for the Super Admin dashboard. The web app names it from <see cref="Key"/>; a source that has no fixed name
/// (a plan, say) gives its own <see cref="Label"/>.
/// </summary>
public sealed record Stat(string Key, long Value, string? Label = null);

/// <summary>A module's contribution to the Super Admin dashboard. A new number is a new source, with no change to Reports.</summary>
public interface IStatsSource
{
    Task<IReadOnlyList<Stat>> ReadAsync(CancellationToken cancellationToken = default);
}
