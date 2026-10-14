using EduVibe.Shared.Modules;

namespace EduVibe.Modules.Exams.Services;

/// <summary>The Excel report of one exam: each student's part marks, total, percentage, grade and rank, by index number.</summary>
internal sealed class ExamResultsReport(ExamReader reader) : IReportSource
{
    public string Key => "exam-results";

    public async Task<ReportResult> BuildAsync(IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
    {
        if (!query.TryGetValue("examId", out var text) || !Guid.TryParse(text, out var examId) ||
            await reader.DetailAsync(examId, cancellationToken) is not { } exam)
        {
            return ReportResult.Fail("exam_not_found");
        }

        var rows = exam.Students.Select(s => (IReadOnlyList<object?>)
        [
            s.IndexNumber, s.FullName,
            .. exam.Parts.Select(p => s.Absent ? null : s.Marks.TryGetValue(p.Id, out var mark) ? mark : (object?)null),
            s.Absent ? "Absent" : s.Total, s.Percent, s.Grade, s.Rank,
        ]).ToList();
        return ReportResult.Ok(new ReportTable(
            $"Results {exam.ClassTitle} {exam.Name}",
            ["Index", "Student", .. exam.Parts.Select(p => $"{p.Name} (/{p.MaxMark:0.##})"), $"Total (/{exam.MaxTotal:0.##})", "Percent", "Grade", "Rank"],
            rows));
    }
}
