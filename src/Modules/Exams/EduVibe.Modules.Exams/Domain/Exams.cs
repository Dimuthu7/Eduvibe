using EduVibe.Shared.Domain;
using EduVibe.Shared.Tenancy;

namespace EduVibe.Modules.Exams.Domain;

/// <summary>
/// How a teacher turns a percentage into a grade. Each teacher has one, made from the O/L default the first time it is needed
/// and editable after that.
/// </summary>
public sealed class GradeScheme : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
}

/// <summary>One grade and the lowest percentage that earns it. The band with the highest minimum the percentage reaches wins.</summary>
public sealed class GradeBand : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid GradeSchemeId { get; set; }
    public required string Grade { get; set; }
    public decimal MinPercent { get; set; }
}

/// <summary>
/// One paper or test of a class on a date, made of one or more parts (for example MCQ out of 40 and an essay out of 60).
/// ClassId belongs to the Classes module and is kept as a plain id.
/// </summary>
public sealed class Exam : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid ClassId { get; set; }
    public Guid GradeSchemeId { get; set; }
    public required string Name { get; set; }
    public DateOnly HeldOn { get; set; }

    /// <summary>When the teacher shared the results. Until then students do not see the exam; after it they see their own marks.</summary>
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class ExamPart : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid ExamId { get; set; }
    public required string Name { get; set; }
    public decimal MaxMark { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// One student's outcome in one exam, recomputed whenever marks are saved so lists load fast. A student with nothing
/// entered has no row. Percent and Rank are set only when the student sat the exam and every part has a mark.
/// EnrolmentId belongs to the Students module and is kept as a plain id.
/// </summary>
public sealed class ExamResult : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid ExamId { get; set; }
    public Guid EnrolmentId { get; set; }

    /// <summary>Did not sit the exam. Different from scoring 0: an absent student has no total and no rank.</summary>
    public bool IsAbsent { get; set; }

    /// <summary>The marks entered so far added together.</summary>
    public decimal Total { get; set; }

    public decimal? Percent { get; set; }
    public int? Rank { get; set; }
}

/// <summary>A mark for one part. A part with no row has not been entered yet.</summary>
public sealed class ExamPartMark : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; set; }
    public Guid ExamResultId { get; set; }
    public Guid ExamPartId { get; set; }
    public decimal Mark { get; set; }
}
