using GuruBook.Modules.Exams.Domain;
using GuruBook.Modules.Exams.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Exams.Endpoints;

/// <summary>Results across exams: one student's trend for the teacher, and a student's own published results.</summary>
internal static class HistoryEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/enrolments/{enrolmentId:guid}/history", History).RequireAuthorization(Policies.Teacher);
        group.MapGet("/mine", Mine).RequireAuthorization(Policies.Student);
        group.MapGet("/children/{studentId:guid}", Child).RequireAuthorization(Policies.Parent);
    }

    // Every exam of the student's class that has something entered for them, oldest first, with the class average beside
    // their own percentage. Classes lists the student's other classes with this teacher so the screen can switch.
    private static async Task<IResult> History(
        Guid enrolmentId, ExamsDbContext db, IClassDirectory classes, IClassRoster roster, CancellationToken ct)
    {
        if (!(await roster.FindEnrolmentsAsync([enrolmentId], ct)).TryGetValue(enrolmentId, out var person))
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        var exams = await db.Exams.AsNoTracking().Where(e => e.ClassId == person.ClassId).ToListAsync(ct);
        var examIds = exams.Select(e => e.Id).ToList();
        var results = await db.Results.AsNoTracking().Where(r => examIds.Contains(r.ExamId)).ToListAsync(ct);
        var parts = await db.Parts.AsNoTracking().Where(p => examIds.Contains(p.ExamId)).ToListAsync(ct);
        var bands = await BandsBySchemeAsync(db, exams.Select(e => e.GradeSchemeId), ignoreTeacher: false, ct);

        var own = results.Where(r => r.EnrolmentId == enrolmentId).ToDictionary(r => r.ExamId);
        var items = exams.Where(e => own.ContainsKey(e.Id))
            .OrderBy(e => e.HeldOn).ThenBy(e => e.CreatedAt)
            .Select(e =>
            {
                var mine = own[e.Id];
                var ranked = results.Where(r => r.ExamId == e.Id && r.Rank is not null).ToList();
                return new HistoryItemDto(
                    e.Id, e.Name, e.HeldOn, parts.Where(p => p.ExamId == e.Id).Sum(p => p.MaxMark), mine.IsAbsent,
                    mine.IsAbsent ? null : mine.Total, mine.Percent, GradeOf(mine, bands.GetValueOrDefault(e.GradeSchemeId)), mine.Rank, ranked.Count,
                    ranked.Count == 0 ? null : ExamCalculator.Round(ranked.Average(r => r.Percent!.Value)), e.PublishedAt);
            })
            .ToArray();

        var enrolments = await roster.EnrolmentsOfStudentAsync(person.StudentId, ct);
        var titles = await classes.FindManyAsync(enrolments.Select(e => e.ClassId), ct);
        var options = enrolments.Where(e => titles.ContainsKey(e.ClassId)).Select(e => new HistoryClassDto(e.EnrolmentId, e.ClassId, titles[e.ClassId].Title)).ToArray();

        return Results.Ok(new StudentHistoryDto(
            person.EnrolmentId, person.StudentId, person.IndexNumber, person.FullName, person.ClassId,
            titles.TryGetValue(person.ClassId, out var cls) ? cls.Title : string.Empty, items, options));
    }

    // The signed-in student's published results in each class they are in, newest first. Only their own row is read,
    // plus how many students are ranked so the rank reads "3 of 25"; no other student's marks leave the server.
    private static async Task<IResult> Mine(ExamsDbContext db, IClassRoster roster, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await LoadPublished(db, await roster.ActiveEnrolmentsOfUserAsync(userId, ct), ct));
    }

    // One child's published results for a parent, in each class whose teacher linked them and has the parent portal.
    private static async Task<IResult> Child(Guid studentId, ExamsDbContext db, IClassRoster roster, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await LoadPublished(db, await roster.ActiveEnrolmentsOfChildAsync(userId, studentId, ct), ct));
    }

    private static async Task<MyExamDto[]> LoadPublished(ExamsDbContext db, IReadOnlyList<MyEnrolment> enrolments, CancellationToken ct)
    {
        var enrolmentIds = enrolments.Select(e => e.EnrolmentId).ToList();
        var classIds = enrolments.Select(e => e.ClassId).ToList();

        // The reader is not a teacher, so the teacher filter is lifted and the enrolments handed in are the only way in.
        var exams = await db.Exams.IgnoreQueryFilters().AsNoTracking()
            .Where(e => classIds.Contains(e.ClassId) && e.PublishedAt != null)
            .ToListAsync(ct);
        var examIds = exams.Select(e => e.Id).ToList();
        var mine = await db.Results.IgnoreQueryFilters().AsNoTracking()
            .Where(r => examIds.Contains(r.ExamId) && enrolmentIds.Contains(r.EnrolmentId))
            .ToListAsync(ct);
        var resultIds = mine.Select(r => r.Id).ToList();
        var marks = await db.PartMarks.IgnoreQueryFilters().AsNoTracking().Where(m => resultIds.Contains(m.ExamResultId)).ToListAsync(ct);
        var parts = await db.Parts.IgnoreQueryFilters().AsNoTracking().Where(p => examIds.Contains(p.ExamId)).ToListAsync(ct);
        var ranked = await db.Results.IgnoreQueryFilters()
            .Where(r => examIds.Contains(r.ExamId) && r.Rank != null)
            .GroupBy(r => r.ExamId).Select(g => new { ExamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ExamId, g => g.Count, ct);
        var bands = await BandsBySchemeAsync(db, exams.Select(e => e.GradeSchemeId), ignoreTeacher: true, ct);

        var byExam = exams.ToDictionary(e => e.Id);
        var items = mine
            .Where(r => enrolments.Any(e => e.EnrolmentId == r.EnrolmentId && e.ClassId == byExam[r.ExamId].ClassId))
            .Select(r =>
            {
                var exam = byExam[r.ExamId];
                var own = marks.Where(m => m.ExamResultId == r.Id).ToDictionary(m => m.ExamPartId, m => m.Mark);
                var examParts = parts.Where(p => p.ExamId == exam.Id).OrderBy(p => p.SortOrder).ToList();
                return new MyExamDto(
                    exam.Id, exam.ClassId, exam.Name, exam.HeldOn, examParts.Sum(p => p.MaxMark), r.IsAbsent,
                    r.IsAbsent || own.Count == 0 ? null : r.Total, r.Percent, GradeOf(r, bands.GetValueOrDefault(exam.GradeSchemeId)), r.Rank,
                    ranked.GetValueOrDefault(exam.Id),
                    [.. examParts.Select(p => new MyPartDto(p.Name, p.MaxMark, r.IsAbsent ? null : own.TryGetValue(p.Id, out var m) ? m : null))]);
            })
            .OrderByDescending(i => i.HeldOn).ThenBy(i => i.Name)
            .ToArray();
        return items;
    }

    /// <summary>A grade only when the student has a percentage, which they have once every part is marked.</summary>
    private static string? GradeOf(ExamResult result, List<BandRule>? bands) =>
        result.Percent is { } percent && bands is not null ? ExamCalculator.GradeFor(percent, bands) : null;

    private static async Task<Dictionary<Guid, List<BandRule>>> BandsBySchemeAsync(
        ExamsDbContext db, IEnumerable<Guid> schemeIds, bool ignoreTeacher, CancellationToken ct)
    {
        var ids = schemeIds.Distinct().ToList();
        var source = ignoreTeacher ? db.GradeBands.IgnoreQueryFilters() : db.GradeBands;
        var bands = await source.AsNoTracking().Where(b => ids.Contains(b.GradeSchemeId)).ToListAsync(ct);
        return bands.GroupBy(b => b.GradeSchemeId).ToDictionary(g => g.Key, g => g.Select(b => new BandRule(b.Grade, b.MinPercent)).ToList());
    }
}
