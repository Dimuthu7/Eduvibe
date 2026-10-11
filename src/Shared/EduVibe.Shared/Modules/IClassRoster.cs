namespace EduVibe.Shared.Modules;

/// <summary>One student in one class, as another module may see them. EnrolmentId is the key attendance and fees hang off.</summary>
public sealed record RosterEntry(Guid EnrolmentId, Guid StudentId, Guid ClassId, int IndexNumber, string FullName);

/// <summary>An enrolment of the signed-in student, across all teachers.</summary>
public sealed record MyEnrolment(Guid EnrolmentId, Guid StudentId, Guid ClassId, Guid TenantId);

/// <summary>
/// Who is in a class. The Students module implements it; Attendance and later modules use it instead of
/// reading the Students tables.
/// </summary>
public interface IClassRoster
{
    /// <summary>The current teacher's students who are active in the class, by index number.</summary>
    Task<IReadOnlyList<RosterEntry>> ActiveRosterAsync(Guid classId, CancellationToken cancellationToken = default);

    /// <summary>The current teacher's enrolments among <paramref name="enrolmentIds"/>, whatever their status (a student who has left keeps their history).</summary>
    Task<IReadOnlyDictionary<Guid, RosterEntry>> FindEnrolmentsAsync(IEnumerable<Guid> enrolmentIds, CancellationToken cancellationToken = default);

    /// <summary>The active enrolments of a student account, across all teachers. For the student's own screens.</summary>
    Task<IReadOnlyList<MyEnrolment>> ActiveEnrolmentsOfUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
