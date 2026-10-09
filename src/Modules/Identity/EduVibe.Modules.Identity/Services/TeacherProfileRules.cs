using EduVibe.Shared.Geography;
using EduVibe.Shared.Modules;

namespace EduVibe.Modules.Identity.Services;

/// <summary>A teacher's profile after the rules have been applied: trimmed names and official district spelling.</summary>
public sealed record CleanProfile(string FirstName, string LastName, string District, Guid StreamId, IReadOnlyList<Guid> SubjectIds);

/// <summary>The rules for a teacher's name, district, stream and subjects, shared by "add teacher" and "edit my details".</summary>
public sealed class TeacherProfileRules(ICatalogDirectory catalog)
{
    public const int MaxNameLength = 60;

    /// <summary>Returns the cleaned profile, or an error code (for example "district_invalid") when a rule fails.</summary>
    public async Task<(CleanProfile? Profile, string? Error)> ValidateAsync(
        string? firstName, string? lastName, string? district, Guid? streamId, Guid[]? subjectIds, CancellationToken ct)
    {
        var first = firstName?.Trim();
        var last = lastName?.Trim();
        if (string.IsNullOrEmpty(first) || first.Length > MaxNameLength)
        {
            return (null, "first_name_required");
        }

        if (string.IsNullOrEmpty(last) || last.Length > MaxNameLength)
        {
            return (null, "last_name_required");
        }

        var canonical = SriLankaDistricts.Canonical(district);
        if (canonical is null)
        {
            return (null, "district_invalid");
        }

        if (streamId is null || !await catalog.StreamIsActiveAsync(streamId.Value, ct))
        {
            return (null, "stream_invalid");
        }

        var subjects = (subjectIds ?? []).Distinct().ToList();
        if (subjects.Count == 0)
        {
            return (null, "subjects_required");
        }

        var known = await catalog.FindActiveSubjectsAsync(subjects, ct);
        return subjects.Any(s => !known.Contains(s))
            ? (null, "subject_invalid")
            : (new CleanProfile(first, last, canonical, streamId.Value, subjects), null);
    }
}
