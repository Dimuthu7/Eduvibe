namespace GuruBook.Shared.Modules;

/// <summary>What other modules may know about a class: enough to enrol into it and to show its name.</summary>
public sealed record ClassSummary(Guid Id, Guid TenantId, string Title, decimal MonthlyFee, bool IsArchived);

/// <summary>A class that meets on a given weekday, with the time it starts and ends (Asia/Colombo local time).</summary>
public sealed record ClassMeeting(Guid ClassId, string Title, TimeOnly Start, TimeOnly End);

/// <summary>One weekly time of a class. Day is 1 (Monday) to 7 (Sunday).</summary>
public sealed record ClassWeeklySlot(int Day, TimeOnly Start, TimeOnly End);

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

    /// <summary>The current teacher's active classes that meet on this weekday (1 Monday to 7 Sunday), one entry per weekly time.</summary>
    Task<IReadOnlyList<ClassMeeting>> MeetingsOnAsync(int dayOfWeek, CancellationToken cancellationToken = default);

    /// <summary>The weekly times of classes of any teacher, for a student's own timetable.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ClassWeeklySlot>>> SlotsInAnyTenantAsync(IEnumerable<Guid> classIds, CancellationToken cancellationToken = default);

    /// <summary>Classes of any teacher among <paramref name="classIds"/>, by id. For background jobs and a student's own screens.</summary>
    Task<IReadOnlyDictionary<Guid, ClassSummary>> FindManyInAnyTenantAsync(IEnumerable<Guid> classIds, CancellationToken cancellationToken = default);
}
