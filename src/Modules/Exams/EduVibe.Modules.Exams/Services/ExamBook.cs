using EduVibe.Modules.Exams.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Exams.Services;

/// <summary>
/// One exam with its parts and every student's marks, held in memory so totals, percentages and ranks are worked out
/// together and saved in one go.
/// </summary>
public sealed class ExamBook
{
    public sealed class Row
    {
        public required ExamResult Result { get; init; }

        /// <summary>The marks entered, by part id.</summary>
        public Dictionary<Guid, ExamPartMark> Marks { get; } = [];
    }

    public required Exam Exam { get; init; }
    public required List<ExamPart> Parts { get; init; }

    /// <summary>By enrolment id.</summary>
    public Dictionary<Guid, Row> Rows { get; } = [];

    public decimal MaxTotal => Parts.Sum(p => p.MaxMark);

    public static async Task<ExamBook?> LoadAsync(ExamsDbContext db, Guid examId, bool track, CancellationToken ct)
    {
        var exams = track ? db.Exams : db.Exams.AsNoTracking();
        var exam = await exams.SingleOrDefaultAsync(e => e.Id == examId, ct);
        if (exam is null)
        {
            return null;
        }

        var parts = await (track ? db.Parts : db.Parts.AsNoTracking()).Where(p => p.ExamId == examId).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var book = new ExamBook { Exam = exam, Parts = parts };
        var results = await (track ? db.Results : db.Results.AsNoTracking()).Where(r => r.ExamId == examId).ToListAsync(ct);
        var resultIds = results.Select(r => r.Id).ToList();
        var marks = await (track ? db.PartMarks : db.PartMarks.AsNoTracking()).Where(m => resultIds.Contains(m.ExamResultId)).ToListAsync(ct);
        var marksByResult = marks.GroupBy(m => m.ExamResultId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var result in results)
        {
            var row = new Row { Result = result };
            foreach (var mark in marksByResult.GetValueOrDefault(result.Id) ?? [])
            {
                row.Marks[mark.ExamPartId] = mark;
            }

            book.Rows[result.EnrolmentId] = row;
        }

        return book;
    }

    public static bool IsComplete(Row row, IReadOnlyCollection<ExamPart> parts) =>
        !row.Result.IsAbsent && parts.All(p => row.Marks.ContainsKey(p.Id));

    /// <summary>Works out every student's total, percentage and rank from the marks held now.</summary>
    public void Recalculate()
    {
        var max = MaxTotal;
        var ranked = new List<(Guid Id, decimal Total)>();
        foreach (var row in Rows.Values)
        {
            var result = row.Result;
            result.Rank = null;
            if (result.IsAbsent)
            {
                result.Total = 0;
                result.Percent = null;
                continue;
            }

            result.Total = ExamCalculator.Round(Parts.Where(p => row.Marks.ContainsKey(p.Id)).Sum(p => row.Marks[p.Id].Mark));
            if (IsComplete(row, Parts))
            {
                result.Percent = ExamCalculator.Percent(result.Total, max);
                ranked.Add((result.Id, result.Total));
            }
            else
            {
                result.Percent = null;
            }
        }

        var ranks = ExamCalculator.Rank(ranked);
        foreach (var row in Rows.Values.Where(r => ranks.ContainsKey(r.Result.Id)))
        {
            row.Result.Rank = ranks[row.Result.Id];
        }
    }
}
