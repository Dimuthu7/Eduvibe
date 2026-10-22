using GuruBook.Modules.Exams.Domain;
using GuruBook.Modules.Exams.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Exams.Endpoints;

public sealed record ExamPartRequest(Guid? Id, string? Name, decimal? MaxMark);

public sealed record SaveExamRequest(Guid? ClassId, string? Name, DateOnly? HeldOn, ExamPartRequest[]? Parts);

public sealed record MarkRowRequest(Guid? EnrolmentId, bool? Absent, Dictionary<Guid, decimal?>? Marks);

public sealed record SaveMarksRequest(MarkRowRequest[]? Rows);

/// <summary>Exams for the teacher: create and change an exam with its parts, enter marks, and read the results.</summary>
internal static class ExamEndpoints
{
    private const int MaxNameLength = 100;
    private const int MaxPartNameLength = 50;
    private const int MaxParts = 10;
    private const decimal MaxPartMark = 1000m;
    private const int MaxRows = 500;
    private const int MaxList = 200;

    public static void Map(RouteGroupBuilder group)
    {
        var exams = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        exams.MapGet("", List);
        exams.MapPost("", Create);
        exams.MapGet("/{id:guid}", Detail);
        exams.MapPut("/{id:guid}", Update);
        exams.MapDelete("/{id:guid}", Delete);
        exams.MapPut("/{id:guid}/marks", SaveMarks);
        exams.MapPost("/{id:guid}/publish", Publish);
        exams.MapPost("/{id:guid}/unpublish", Unpublish);
    }

    // The teacher's exams, newest first, optionally for one class. Counts and the average come from the saved results.
    private static async Task<IResult> List(
        Guid? classId, int? limit, ExamsDbContext db, IClassDirectory classes, IClassRoster roster, CancellationToken ct)
    {
        var query = db.Exams.AsNoTracking().AsQueryable();
        if (classId is { } id)
        {
            query = query.Where(e => e.ClassId == id);
        }

        // SQLite (used in tests) cannot order by a date it stores as text with a second key, so the order is applied after loading.
        var exams = (await query.ToListAsync(ct))
            .OrderByDescending(e => e.HeldOn).ThenByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(limit ?? MaxList, 1, MaxList)).ToList();
        if (exams.Count == 0)
        {
            return Results.Ok(Array.Empty<ExamListItemDto>());
        }

        var examIds = exams.Select(e => e.Id).ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => examIds.Contains(p.ExamId)).ToListAsync(ct);
        var results = await db.Results.AsNoTracking().Where(r => examIds.Contains(r.ExamId)).Select(r => new { r.ExamId, r.IsAbsent, r.Percent }).ToListAsync(ct);
        var titles = await classes.FindManyAsync(exams.Select(e => e.ClassId).Distinct(), ct);
        var sizes = new Dictionary<Guid, int>();
        foreach (var cid in exams.Select(e => e.ClassId).Distinct())
        {
            sizes[cid] = (await roster.ActiveRosterAsync(cid, ct)).Count;
        }

        var items = exams.Select(e =>
        {
            var own = results.Where(r => r.ExamId == e.Id).ToList();
            var scored = own.Where(r => r.Percent is not null).ToList();
            var ownParts = parts.Where(p => p.ExamId == e.Id).ToList();
            return new ExamListItemDto(
                e.Id, e.ClassId, titles.TryGetValue(e.ClassId, out var cls) ? cls.Title : string.Empty, e.Name, e.HeldOn,
                ownParts.Sum(p => p.MaxMark), ownParts.Count, sizes[e.ClassId], own.Count(r => r.IsAbsent || r.Percent is not null),
                scored.Count == 0 ? null : ExamCalculator.Round(scored.Average(r => r.Percent!.Value)), e.PublishedAt);
        });
        return Results.Ok(items);
    }

    private static async Task<IResult> Detail(Guid id, ExamReader reader, CancellationToken ct)
    {
        var detail = await reader.DetailAsync(id, ct);
        return detail is null ? Problems.Reject("exam_not_found", StatusCodes.Status404NotFound) : Results.Ok(detail);
    }

    private static async Task<IResult> Create(
        SaveExamRequest request, ExamsDbContext db, GradeSchemeService schemes, IClassDirectory classes, ExamReader reader, CancellationToken ct)
    {
        if (Validate(request) is { } error)
        {
            return Problems.Reject(error);
        }

        var cls = request.ClassId is { } classId ? await classes.FindAsync(classId, ct) : null;
        if (cls is null)
        {
            return Problems.Reject("class_not_found", StatusCodes.Status404NotFound);
        }

        var scheme = await schemes.EnsureAsync(ct);
        var exam = new Exam { ClassId = cls.Id, GradeSchemeId = scheme.Id, Name = Problems.Clean(request.Name)!, HeldOn = request.HeldOn!.Value };
        db.Exams.Add(exam);
        db.Parts.AddRange(NewParts(exam.Id, request.Parts!));
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/exams/{exam.Id}", await reader.DetailAsync(exam.Id, ct));
    }

    // Name and date can always change. The parts can be added, removed or reordered only while no marks exist;
    // after that they are matched by id, so a rename or a new maximum cannot orphan a mark.
    private static async Task<IResult> Update(Guid id, SaveExamRequest request, ExamsDbContext db, ExamReader reader, CancellationToken ct)
    {
        if (Validate(request) is { } error)
        {
            return Problems.Reject(error);
        }

        var book = await ExamBook.LoadAsync(db, id, track: true, ct);
        if (book is null)
        {
            return Problems.Reject("exam_not_found", StatusCodes.Status404NotFound);
        }

        book.Exam.Name = Problems.Clean(request.Name)!;
        book.Exam.HeldOn = request.HeldOn!.Value;

        var requested = request.Parts!;
        if (book.Rows.Count == 0)
        {
            db.Parts.RemoveRange(book.Parts);
            db.Parts.AddRange(NewParts(book.Exam.Id, requested));
        }
        else
        {
            var ids = requested.Select(p => p.Id).ToList();
            if (ids.Any(i => i is null) || ids.Distinct().Count() != ids.Count || !ids.ToHashSet().SetEquals(book.Parts.Select(p => (Guid?)p.Id)))
            {
                return Problems.Reject("parts_locked");
            }

            for (var i = 0; i < requested.Length; i++)
            {
                var part = book.Parts.Single(p => p.Id == requested[i].Id);
                var max = requested[i].MaxMark!.Value;
                if (book.Rows.Values.Any(r => r.Marks.TryGetValue(part.Id, out var mark) && mark.Mark > max))
                {
                    return Problems.Reject("max_below_marks");
                }

                part.Name = requested[i].Name!.Trim();
                part.MaxMark = max;
                part.SortOrder = i;
            }

            book.Recalculate();
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await reader.DetailAsync(id, ct));
    }

    private static async Task<IResult> Delete(Guid id, ExamsDbContext db, CancellationToken ct)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null)
        {
            return Problems.Reject("exam_not_found", StatusCodes.Status404NotFound);
        }

        db.Exams.Remove(exam);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // Shares the results: each student in the class then sees their own marks, grade and rank. Marks can still be changed
    // after this, and students see the change. Publishing again keeps the first date.
    private static async Task<IResult> Publish(
        Guid id, ExamsDbContext db, ExamReader reader, IClassRoster roster, IClassDirectory classes, INotifier notifier, TimeProvider clock, CancellationToken ct) =>
        await SetPublished(id, db, reader, clock.GetUtcNow(), (roster, classes, notifier), ct);

    private static async Task<IResult> Unpublish(Guid id, ExamsDbContext db, ExamReader reader, CancellationToken ct) =>
        await SetPublished(id, db, reader, null, null, ct);

    private static async Task<IResult> SetPublished(
        Guid id, ExamsDbContext db, ExamReader reader, DateTimeOffset? at, (IClassRoster Roster, IClassDirectory Classes, INotifier Notifier)? tell, CancellationToken ct)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (exam is null)
        {
            return Problems.Reject("exam_not_found", StatusCodes.Status404NotFound);
        }

        if (at is null || exam.PublishedAt is null)
        {
            exam.PublishedAt = at;
            await db.SaveChangesAsync(ct);
            if (at is not null && tell is { } who)
            {
                // Students are told when results first go out. The key is the exam, so publishing again after taking it back tells nobody twice.
                var title = (await who.Classes.FindAsync(exam.ClassId, ct))?.Title ?? "";
                var roster = await who.Roster.ActiveRosterAsync(exam.ClassId, ct);
                await who.Notifier.NotifyAsync(
                    [.. roster.Where(r => r.UserId is not null).Select(r => new NotifyRequest(
                        r.UserId!.Value, "results_published", new { exam = exam.Name, classTitle = title }, Link: "/results", OnceKey: $"results:{exam.Id}"))],
                    ct);
            }
        }

        return Results.Ok(await reader.DetailAsync(id, ct));
    }

    // Saves the rows sent and nothing else. A row replaces that student's marks: parts left out or null are not entered.
    // A student who is absent has no marks; a row with no marks and not absent clears the student.
    private static async Task<IResult> SaveMarks(
        Guid id, SaveMarksRequest request, ExamsDbContext db, IClassRoster roster, ExamReader reader, CancellationToken ct)
    {
        var rows = request.Rows ?? [];
        if (rows.Length == 0 || rows.Length > MaxRows || rows.Any(r => r.EnrolmentId is null) || rows.Select(r => r.EnrolmentId).Distinct().Count() != rows.Length)
        {
            return Problems.Reject("marks_invalid");
        }

        var book = await ExamBook.LoadAsync(db, id, track: true, ct);
        if (book is null)
        {
            return Problems.Reject("exam_not_found", StatusCodes.Status404NotFound);
        }

        var enrolmentIds = rows.Select(r => r.EnrolmentId!.Value).ToList();
        var known = await roster.FindEnrolmentsAsync(enrolmentIds, ct);
        var active = (await roster.ActiveRosterAsync(book.Exam.ClassId, ct)).Select(r => r.EnrolmentId).ToHashSet();
        var parts = book.Parts.ToDictionary(p => p.Id);

        foreach (var row in rows)
        {
            var enrolmentId = row.EnrolmentId!.Value;
            if (!known.TryGetValue(enrolmentId, out var person) || person.ClassId != book.Exam.ClassId
                || (!active.Contains(enrolmentId) && !book.Rows.ContainsKey(enrolmentId)))
            {
                return Problems.Reject("student_not_in_class");
            }

            foreach (var (partId, value) in row.Marks ?? [])
            {
                if (!parts.TryGetValue(partId, out var part))
                {
                    return Problems.Reject("part_not_found");
                }

                if (value is { } mark && (mark < 0 || mark > part.MaxMark || decimal.Round(mark, 2) != mark))
                {
                    return Problems.Reject("mark_invalid");
                }
            }
        }

        foreach (var row in rows)
        {
            Apply(db, book, row);
        }

        book.Recalculate();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await reader.DetailAsync(book, ct));
    }

    private static void Apply(ExamsDbContext db, ExamBook book, MarkRowRequest request)
    {
        var enrolmentId = request.EnrolmentId!.Value;
        var entered = (request.Marks ?? []).Where(m => m.Value is not null).ToDictionary(m => m.Key, m => m.Value!.Value);
        var absent = request.Absent == true;
        book.Rows.TryGetValue(enrolmentId, out var row);

        if (!absent && entered.Count == 0)
        {
            if (row is not null)
            {
                db.PartMarks.RemoveRange(row.Marks.Values);
                db.Results.Remove(row.Result);
                book.Rows.Remove(enrolmentId);
            }

            return;
        }

        if (row is null)
        {
            row = new ExamBook.Row { Result = new ExamResult { ExamId = book.Exam.Id, EnrolmentId = enrolmentId } };
            db.Results.Add(row.Result);
            book.Rows[enrolmentId] = row;
        }

        row.Result.IsAbsent = absent;
        if (absent)
        {
            entered.Clear();
        }

        foreach (var (partId, mark) in row.Marks.Where(m => !entered.ContainsKey(m.Key)).ToList())
        {
            db.PartMarks.Remove(mark);
            row.Marks.Remove(partId);
        }

        foreach (var (partId, value) in entered)
        {
            if (row.Marks.TryGetValue(partId, out var existing))
            {
                existing.Mark = value;
            }
            else
            {
                var created = new ExamPartMark { ExamResultId = row.Result.Id, ExamPartId = partId, Mark = value };
                db.PartMarks.Add(created);
                row.Marks[partId] = created;
            }
        }
    }

    private static IEnumerable<ExamPart> NewParts(Guid examId, ExamPartRequest[] parts) =>
        parts.Select((p, i) => new ExamPart { ExamId = examId, Name = p.Name!.Trim(), MaxMark = p.MaxMark!.Value, SortOrder = i });

    /// <summary>The first thing wrong with an exam form, as an error code, or null when it is fine.</summary>
    private static string? Validate(SaveExamRequest request)
    {
        var name = Problems.Clean(request.Name);
        if (name is null)
        {
            return "name_required";
        }

        if (name.Length > MaxNameLength)
        {
            return "name_too_long";
        }

        if (request.HeldOn is null)
        {
            return "held_on_required";
        }

        var parts = request.Parts ?? [];
        if (parts.Length is 0 or > MaxParts)
        {
            return "parts_invalid";
        }

        foreach (var part in parts)
        {
            var partName = Problems.Clean(part.Name);
            if (partName is null)
            {
                return "part_name_required";
            }

            if (partName.Length > MaxPartNameLength)
            {
                return "part_name_too_long";
            }

            if (part.MaxMark is not { } max || max <= 0 || max > MaxPartMark || decimal.Round(max, 2) != max)
            {
                return "part_max_invalid";
            }
        }

        return parts.Select(p => p.Name!.Trim().ToUpperInvariant()).Distinct().Count() == parts.Length ? null : "part_name_duplicate";
    }
}
