namespace EduVibe.Shared.Modules;

/// <summary>
/// What other modules may ask Attendance. Attendance implements it; Fees uses it for the "attended but unpaid" view
/// without reading the attendance tables.
/// </summary>
public interface IAttendanceLookup
{
    /// <summary>
    /// How many classes each of the current teacher's students attended (present or late) between the two dates, both included,
    /// counting only classes whose attendance was saved. Students who attended none are left out. <paramref name="classId"/> limits it to one class.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> AttendedCountsAsync(DateOnly from, DateOnly to, Guid? classId, CancellationToken cancellationToken = default);
}
