using GuruBook.Shared.Domain;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Attendance.Domain;

/// <summary>
/// One meeting of a class on one date. Sessions are made from the class's weekly times when the teacher opens
/// a day, or added by hand as an extra class. ClassId belongs to the Classes module and is kept as a plain id.
/// </summary>
public sealed class ClassSession : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }

    /// <summary>The date in Sri Lanka.</summary>
    public DateOnly Date { get; set; }

    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    /// <summary>True for a class added by hand rather than made from the weekly timetable.</summary>
    public bool IsExtra { get; set; }

    public string? Note { get; set; }

    /// <summary>Set when the teacher first saves attendance. Only taken sessions count in the percentages.</summary>
    public DateTimeOffset? TakenAt { get; set; }
}

public enum AttendanceStatus
{
    Present,
    Absent,
    Late,
}

/// <summary>One student's attendance at one session. EnrolmentId belongs to the Students module and is kept as a plain id.</summary>
public sealed class AttendanceMark : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public Guid EnrolmentId { get; set; }
    public AttendanceStatus Status { get; set; }
}
