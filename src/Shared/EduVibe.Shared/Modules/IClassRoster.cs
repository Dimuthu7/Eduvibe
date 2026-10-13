namespace EduVibe.Shared.Modules;

/// <summary>One student in one class, as another module may see them. EnrolmentId is the key attendance and fees hang off.</summary>
public sealed record RosterEntry(Guid EnrolmentId, Guid StudentId, Guid ClassId, int IndexNumber, string FullName);

/// <summary>An enrolment of the signed-in student, across all teachers.</summary>
public sealed record MyEnrolment(Guid EnrolmentId, Guid StudentId, Guid ClassId, Guid TenantId);

/// <summary>
/// What fees need to know about one enrolment: who they are and how much of the class fee they pay.
/// FeePlan is "Full", "Discount" or "FreeCard". Discount is LKR off the monthly fee when the plan is "Discount".
/// </summary>
public sealed record FeeBasis(
    Guid EnrolmentId, Guid StudentId, Guid TenantId, Guid ClassId, string FullName, int IndexNumber,
    string FeePlan, decimal DiscountAmount, DateOnly JoinedOn, bool IsActive);

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

    /// <summary>All of the current teacher's enrolments of one student, whatever their status: one per class the student has been in.</summary>
    Task<IReadOnlyList<RosterEntry>> EnrolmentsOfStudentAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>The active enrolments of a student account, across all teachers. For the student's own screens.</summary>
    Task<IReadOnlyList<MyEnrolment>> ActiveEnrolmentsOfUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The current teacher's active enrolments for fees, optionally for one class.</summary>
    Task<IReadOnlyList<FeeBasis>> FeeBasisAsync(Guid? classId, CancellationToken cancellationToken = default);

    /// <summary>One of the current teacher's enrolments for fees, whatever its status, or null.</summary>
    Task<FeeBasis?> FindFeeBasisAsync(Guid enrolmentId, CancellationToken cancellationToken = default);

    /// <summary>Every teacher's active enrolments. Only for background jobs, which act for no teacher.</summary>
    Task<IReadOnlyList<FeeBasis>> FeeBasisForAllTeachersAsync(CancellationToken cancellationToken = default);
}
