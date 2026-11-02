using GuruBook.Shared.Domain;
using GuruBook.Shared.Tenancy;

namespace GuruBook.Modules.Fees.Domain;

/// <summary>How fee reminders work for one class. A class with no row has them on, weekly until paid.</summary>
public sealed class ClassReminderSetting : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }

    /// <summary>False stops every automatic reminder for the class.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many weekly reminders follow the due date, 1 to 8. Null means every week until the fee is paid.</summary>
    public int? WeeklyLimit { get; set; }
}

/// <summary>A student the teacher does not want reminded (for example, one who pays in person on a fixed day).</summary>
public sealed class ReminderSkip : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid EnrolmentId { get; set; }
}

/// <summary>
/// A reminder that went out for a fee. It is also what stops the same step going out twice: a step is sent once per fee,
/// whatever happens to the notification itself afterwards.
/// </summary>
public sealed class FeeReminder : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid DueId { get; set; }
    public Guid EnrolmentId { get; set; }
    public Guid ClassId { get; set; }

    /// <summary>"before" the period starts, "due" on the due date, or "late" for a weekly reminder after it.</summary>
    public required string Step { get; set; }

    /// <summary>For a late reminder, how many weeks after the due date it was for (1, 2, 3...). Zero for the others.</summary>
    public int Number { get; set; }

    /// <summary>How it was sent: "App" (the bell, and the phone when the student turned that on). More channels come later.</summary>
    public required string Channel { get; set; }
}
