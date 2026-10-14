namespace EduVibe.Modules.Exams.Services;

public sealed record BandDto(string Grade, decimal MinPercent);

public sealed record GradeSchemeDto(string Name, BandDto[] Bands);

public sealed record PartDto(Guid Id, string Name, decimal MaxMark);

/// <summary>
/// One student in an exam. Marks holds the parts entered so far by part id. Total, Percent, Grade and Rank are set only
/// once the student has a mark for every part; an absent student has none of them.
/// </summary>
public sealed record StudentResultDto(
    Guid EnrolmentId, int IndexNumber, string FullName, bool IsActive, bool Absent, Dictionary<Guid, decimal> Marks,
    decimal? Total, decimal? Percent, string? Grade, int? Rank);

public sealed record ExamSummaryDto(int Students, int Marked, decimal? AveragePercent);

public sealed record ExamDetailDto(
    Guid Id, Guid ClassId, string ClassTitle, string Name, DateOnly HeldOn, decimal MaxTotal,
    PartDto[] Parts, BandDto[] Bands, ExamSummaryDto Summary, StudentResultDto[] Students);

public sealed record ExamListItemDto(
    Guid Id, Guid ClassId, string ClassTitle, string Name, DateOnly HeldOn, decimal MaxTotal, int Parts,
    int Students, int Marked, decimal? AveragePercent);
