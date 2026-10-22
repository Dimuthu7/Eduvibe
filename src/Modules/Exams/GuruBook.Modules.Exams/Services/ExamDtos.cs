namespace GuruBook.Modules.Exams.Services;

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

/// <summary>The average mark of one part, over the students who have a mark for it. Null when nobody has.</summary>
public sealed record PartAverageDto(Guid PartId, string Name, decimal MaxMark, decimal? Average);

/// <summary>How the class did. Highest, Lowest, Average and Median are totals out of the exam's maximum; see <see cref="ExamStats"/>.</summary>
public sealed record ExamAnalysisDto(
    int Sat, int Absent, decimal? Highest, decimal? Lowest, decimal? Average, decimal? Median, decimal? PassMark, int Passed, int? PassRate,
    GradeCount[] Grades, PartAverageDto[] Parts);

public sealed record ExamDetailDto(
    Guid Id, Guid ClassId, string ClassTitle, string Name, DateOnly HeldOn, decimal MaxTotal, DateTimeOffset? PublishedAt,
    PartDto[] Parts, BandDto[] Bands, ExamSummaryDto Summary, ExamAnalysisDto Analysis, StudentResultDto[] Students);

public sealed record ExamListItemDto(
    Guid Id, Guid ClassId, string ClassTitle, string Name, DateOnly HeldOn, decimal MaxTotal, int Parts,
    int Students, int Marked, decimal? AveragePercent, DateTimeOffset? PublishedAt);

/// <summary>
/// One exam in a student's history. Ranked is how many students have a rank in it, so the rank reads "3 of 25".
/// ClassAverage is the average percentage of those students.
/// </summary>
public sealed record HistoryItemDto(
    Guid ExamId, string Name, DateOnly HeldOn, decimal MaxTotal, bool Absent, decimal? Total, decimal? Percent, string? Grade,
    int? Rank, int Ranked, decimal? ClassAverage, DateTimeOffset? PublishedAt);

public sealed record HistoryClassDto(Guid EnrolmentId, Guid ClassId, string Title);

/// <summary>One student's results across every exam of one class, oldest first, and their other classes with the same teacher.</summary>
public sealed record StudentHistoryDto(
    Guid EnrolmentId, Guid StudentId, int IndexNumber, string FullName, Guid ClassId, string ClassTitle,
    HistoryItemDto[] Exams, HistoryClassDto[] Classes);

public sealed record MyPartDto(string Name, decimal MaxMark, decimal? Mark);

/// <summary>A published exam as the student sees it: only their own marks, grade and rank.</summary>
public sealed record MyExamDto(
    Guid ExamId, Guid ClassId, string Name, DateOnly HeldOn, decimal MaxTotal, bool Absent, decimal? Total, decimal? Percent,
    string? Grade, int? Rank, int Ranked, MyPartDto[] Parts);
