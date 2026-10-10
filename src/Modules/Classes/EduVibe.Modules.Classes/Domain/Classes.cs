using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;

namespace EduVibe.Modules.Classes.Domain;

/// <summary>A place that is not a registered institute, such as the teacher's home or a hired hall. Only its teacher sees it.</summary>
public sealed class Venue : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string? District { get; set; }
    public string? Town { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum ClassStatus
{
    Active,
    Archived,
}

/// <summary>
/// One class a teacher runs, for example "2027 A/L Combined Maths" at one institute or venue.
/// SubjectId and StreamId belong to the Catalog module; InstituteId to this module's institutes.
/// </summary>
public sealed class TuitionClass : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public required string Title { get; set; }
    public Guid SubjectId { get; set; }
    public Guid StreamId { get; set; }

    /// <summary>The exam year the class prepares for, for example 2027.</summary>
    public int ExamYear { get; set; }

    public required string Medium { get; set; }

    /// <summary>Exactly one of InstituteId and VenueId is set.</summary>
    public Guid? InstituteId { get; set; }
    public Guid? VenueId { get; set; }

    /// <summary>Monthly fee in LKR.</summary>
    public decimal MonthlyFee { get; set; }

    public ClassStatus Status { get; set; } = ClassStatus.Active;
    public DateTimeOffset? ArchivedAt { get; set; }

    public List<ClassSlot> Slots { get; set; } = [];
}

/// <summary>A weekly time the class meets. Day is 1 (Monday) to 7 (Sunday); times are Asia/Colombo local time.</summary>
public sealed class ClassSlot : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }
    public int Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}

public static class ClassMediums
{
    public static readonly string[] All = ["Sinhala", "Tamil", "English"];

    public static string? Canonical(string? value) =>
        All.FirstOrDefault(m => string.Equals(m, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
