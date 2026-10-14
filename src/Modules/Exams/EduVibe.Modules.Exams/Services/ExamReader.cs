using EduVibe.Shared.Modules;

namespace EduVibe.Modules.Exams.Services;

/// <summary>Builds the exam screens' data: the exam, who is in the class, and each student's outcome.</summary>
public sealed class ExamReader(ExamsDbContext db, GradeSchemeService schemes, IClassDirectory classes, IClassRoster roster)
{
    public async Task<ExamDetailDto?> DetailAsync(Guid examId, CancellationToken ct)
    {
        var book = await ExamBook.LoadAsync(db, examId, track: false, ct);
        return book is null ? null : await DetailAsync(book, ct);
    }

    public async Task<ExamDetailDto> DetailAsync(ExamBook book, CancellationToken ct)
    {
        var exam = book.Exam;
        var bands = await schemes.BandsAsync(exam.GradeSchemeId, ct);
        var cls = await classes.FindAsync(exam.ClassId, ct);

        // Students still in the class, plus any who left but have marks, so their history stays visible.
        var people = (await roster.ActiveRosterAsync(exam.ClassId, ct)).ToDictionary(r => r.EnrolmentId);
        var activeIds = people.Keys.ToHashSet();
        var leftIds = book.Rows.Keys.Where(id => !people.ContainsKey(id)).ToList();
        if (leftIds.Count > 0)
        {
            foreach (var (id, entry) in await roster.FindEnrolmentsAsync(leftIds, ct))
            {
                people[id] = entry;
            }
        }

        var students = people.Values.OrderBy(p => p.IndexNumber).Select(p =>
        {
            if (!book.Rows.TryGetValue(p.EnrolmentId, out var row))
            {
                return new StudentResultDto(p.EnrolmentId, p.IndexNumber, p.FullName, activeIds.Contains(p.EnrolmentId), false, [], null, null, null, null);
            }

            var complete = ExamBook.IsComplete(row, book.Parts);
            var marks = row.Marks.ToDictionary(m => m.Key, m => m.Value.Mark);
            var percent = row.Result.Percent;
            return new StudentResultDto(
                p.EnrolmentId, p.IndexNumber, p.FullName, activeIds.Contains(p.EnrolmentId), row.Result.IsAbsent, marks,
                row.Result.IsAbsent || marks.Count == 0 ? null : row.Result.Total,
                percent, complete && percent is { } value ? ExamCalculator.GradeFor(value, bands) : null, row.Result.Rank);
        }).ToArray();

        var scored = students.Where(s => s.Percent is not null).ToList();
        var summary = new ExamSummaryDto(
            activeIds.Count, students.Count(s => s.Absent || s.Percent is not null),
            scored.Count == 0 ? null : ExamCalculator.Round(scored.Average(s => s.Percent!.Value)));

        return new ExamDetailDto(
            exam.Id, exam.ClassId, cls?.Title ?? string.Empty, exam.Name, exam.HeldOn, book.MaxTotal,
            [.. book.Parts.Select(p => new PartDto(p.Id, p.Name, p.MaxMark))],
            [.. bands.Select(b => new BandDto(b.Grade, b.MinPercent))], summary, students);
    }
}
