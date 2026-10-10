using EduVibe.Modules.Classes.Domain;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Classes.Endpoints;

public sealed record SlotInput(int? Day, TimeOnly? Start, TimeOnly? End);

public sealed record ClassRequest(
    string? Title, Guid? SubjectId, Guid? StreamId, int? ExamYear, string? Medium,
    Guid? InstituteId, Guid? VenueId, decimal? MonthlyFee, SlotInput[]? Slots);

public sealed record SlotDto(Guid Id, int Day, TimeOnly Start, TimeOnly End);

/// <summary>PlaceType is "institute" or "venue"; PlaceName is the name shown on the class.</summary>
public sealed record ClassDto(
    Guid Id, string Title, Guid SubjectId, Guid StreamId, int ExamYear, string Medium,
    string PlaceType, Guid PlaceId, string PlaceName, Guid? InstituteId, decimal MonthlyFee, string Status,
    DateTimeOffset? ArchivedAt, SlotDto[] Slots);

/// <summary>One weekly slot on the timetable. OverlapsWith lists the other classes that meet at the same time.</summary>
public sealed record TimetableEntryDto(
    Guid ClassId, string Title, Guid SubjectId, int Day, TimeOnly Start, TimeOnly End,
    string PlaceType, Guid PlaceId, string PlaceName, Guid? InstituteId, Guid[] OverlapsWith);

internal static class ClassEndpoints
{
    private const int MinYear = 2000;
    private const int MaxYear = 2100;
    private const decimal MaxFee = 10_000_000m;

    public static void Map(RouteGroupBuilder group)
    {
        var classes = group.MapGroup("/classes").RequireAuthorization(Policies.Teacher);
        classes.MapGet("", List);
        classes.MapGet("/{id:guid}", Get);
        classes.MapPost("", Create);
        classes.MapPut("/{id:guid}", Update);
        classes.MapPost("/{id:guid}/archive", Archive);
        classes.MapPost("/{id:guid}/restore", Restore);

        group.MapGet("/timetable", Timetable).RequireAuthorization(Policies.Teacher);
    }

    // status: active (default), archived or all; instituteId narrows to one institute.
    private static async Task<IResult> List(
        string? status, Guid? instituteId, ClassesDbContext db, CancellationToken ct)
    {
        var query = db.Classes.Include(c => c.Slots).AsQueryable();
        query = status?.ToLowerInvariant() switch
        {
            "archived" => query.Where(c => c.Status == ClassStatus.Archived),
            "all" => query,
            _ => query.Where(c => c.Status == ClassStatus.Active),
        };
        if (instituteId is not null)
        {
            query = query.Where(c => c.InstituteId == instituteId);
        }

        var rows = await query.OrderBy(c => c.Title).ToListAsync(ct);
        var places = await PlaceNames.LoadAsync(db, ct);
        return Results.Ok(rows.Select(c => ToDto(c, places)).ToList());
    }

    private static async Task<IResult> Get(Guid id, ClassesDbContext db, CancellationToken ct)
    {
        var row = await db.Classes.Include(c => c.Slots).SingleOrDefaultAsync(c => c.Id == id, ct);
        return row is null ? Results.NotFound() : Results.Ok(ToDto(row, await PlaceNames.LoadAsync(db, ct)));
    }

    private static async Task<IResult> Create(
        ClassRequest request, ITenantContext tenant, ClassesDbContext db, ICatalogDirectory catalog, CancellationToken ct)
    {
        if (await Validate(request, tenant, db, catalog, ct) is { } problem)
        {
            return problem;
        }

        var row = new TuitionClass { Title = request.Title!.Trim(), Medium = ClassMediums.Canonical(request.Medium)! };
        Apply(row, request);
        db.Classes.Add(row);
        AddSlots(db, row, request);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/classes/classes/{row.Id}", ToDto(row, await PlaceNames.LoadAsync(db, ct)));
    }

    private static async Task<IResult> Update(
        Guid id, ClassRequest request, ITenantContext tenant, ClassesDbContext db, ICatalogDirectory catalog, CancellationToken ct)
    {
        var row = await db.Classes.Include(c => c.Slots).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (row is null)
        {
            return Results.NotFound();
        }

        if (row.Status == ClassStatus.Archived)
        {
            return Problems.Reject("class_archived", StatusCodes.Status409Conflict);
        }

        if (await Validate(request, tenant, db, catalog, ct) is { } problem)
        {
            return problem;
        }

        row.Title = request.Title!.Trim();
        row.Medium = ClassMediums.Canonical(request.Medium)!;
        // Slots are replaced as a set: delete the old rows and add the new ones next to the class.
        db.ClassSlots.RemoveRange(row.Slots.ToList());
        Apply(row, request);
        AddSlots(db, row, request);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(row, await PlaceNames.LoadAsync(db, ct)));
    }

    private static async Task<IResult> Archive(Guid id, TimeProvider clock, ClassesDbContext db, CancellationToken ct) =>
        await SetStatus(id, ClassStatus.Archived, clock.GetUtcNow(), db, ct);

    private static async Task<IResult> Restore(Guid id, TimeProvider clock, ClassesDbContext db, CancellationToken ct) =>
        await SetStatus(id, ClassStatus.Active, null, db, ct);

    private static async Task<IResult> SetStatus(Guid id, ClassStatus status, DateTimeOffset? archivedAt, ClassesDbContext db, CancellationToken ct)
    {
        var row = await db.Classes.Include(c => c.Slots).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (row is null)
        {
            return Results.NotFound();
        }

        row.Status = status;
        row.ArchivedAt = archivedAt;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(row, await PlaceNames.LoadAsync(db, ct)));
    }

    // The weekly timetable of active classes, by day and time, with the classes that clash flagged.
    private static async Task<IResult> Timetable(Guid? instituteId, ClassesDbContext db, CancellationToken ct)
    {
        var rows = await db.Classes.Include(c => c.Slots).Where(c => c.Status == ClassStatus.Active).ToListAsync(ct);
        var places = await PlaceNames.LoadAsync(db, ct);

        var all = rows.SelectMany(c => c.Slots.Select(s => (Class: c, Slot: s))).ToList();
        var entries = all
            .Where(x => instituteId is null || x.Class.InstituteId == instituteId)
            .Select(x => new TimetableEntryDto(
                x.Class.Id, x.Class.Title, x.Class.SubjectId, x.Slot.Day, x.Slot.Start, x.Slot.End,
                PlaceType(x.Class), PlaceId(x.Class), places.Name(x.Class), x.Class.InstituteId,
                // A clash counts even when the other class is at a different institute: the teacher cannot be in both.
                [.. all.Where(o => o.Class.Id != x.Class.Id && o.Slot.Day == x.Slot.Day && o.Slot.Start < x.Slot.End && x.Slot.Start < o.Slot.End)
                    .Select(o => o.Class.Id).Distinct()]))
            .OrderBy(e => e.Day).ThenBy(e => e.Start).ThenBy(e => e.Title)
            .ToList();
        return Results.Ok(entries);
    }

    private static async Task<IResult?> Validate(
        ClassRequest request, ITenantContext tenant, ClassesDbContext db, ICatalogDirectory catalog, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 120)
        {
            return Problems.Reject("title_required");
        }

        if (request.SubjectId is not { } subject || !(await catalog.FindActiveSubjectsAsync([subject], ct)).Contains(subject))
        {
            return Problems.Reject("subject_invalid");
        }

        if (request.StreamId is not { } stream || !await catalog.StreamIsActiveAsync(stream, ct))
        {
            return Problems.Reject("stream_invalid");
        }

        if (request.ExamYear is not { } year || year < MinYear || year > MaxYear)
        {
            return Problems.Reject("exam_year_invalid");
        }

        if (ClassMediums.Canonical(request.Medium) is null)
        {
            return Problems.Reject("medium_invalid");
        }

        if (request.MonthlyFee is not { } fee || fee < 0 || fee > MaxFee)
        {
            return Problems.Reject("fee_invalid");
        }

        if (request.InstituteId is null == (request.VenueId is null))
        {
            return Problems.Reject("place_required");
        }

        // Only a place this teacher can use: an institute they are linked to, or one of their own venues.
        var teacherId = tenant.TenantId!.Value;
        var placeOk = request.InstituteId is { } instituteId
            ? await db.Institutes.AnyAsync(i => i.Id == instituteId && i.IsActive
                && db.InstituteTeachers.Any(l => l.InstituteId == i.Id && l.TeacherId == teacherId), ct)
            : await db.Venues.AnyAsync(v => v.Id == request.VenueId && v.IsActive, ct);
        if (!placeOk)
        {
            return Problems.Reject("place_invalid");
        }

        return ValidateSlots(request.Slots);
    }

    private static IResult? ValidateSlots(SlotInput[]? slots)
    {
        if (slots is null || slots.Length == 0)
        {
            return Problems.Reject("slots_required");
        }

        if (slots.Any(s => s.Day is not (>= 1 and <= 7) || s.Start is null || s.End is null || s.Start >= s.End))
        {
            return Problems.Reject("slot_invalid");
        }

        var clash = slots.Any(a => slots.Any(b => !ReferenceEquals(a, b) && a.Day == b.Day && a.Start < b.End && b.Start < a.End));
        return clash ? Problems.Reject("slots_overlap") : null;
    }

    private static void Apply(TuitionClass row, ClassRequest request)
    {
        row.SubjectId = request.SubjectId!.Value;
        row.StreamId = request.StreamId!.Value;
        row.ExamYear = request.ExamYear!.Value;
        row.InstituteId = request.InstituteId;
        row.VenueId = request.VenueId;
        row.MonthlyFee = request.MonthlyFee!.Value;
    }

    private static void AddSlots(ClassesDbContext db, TuitionClass row, ClassRequest request)
    {
        foreach (var s in request.Slots!.OrderBy(s => s.Day).ThenBy(s => s.Start))
        {
            db.ClassSlots.Add(new ClassSlot { ClassId = row.Id, Day = s.Day!.Value, Start = s.Start!.Value, End = s.End!.Value });
        }
    }

    private static string PlaceType(TuitionClass c) => c.InstituteId is null ? "venue" : "institute";
    private static Guid PlaceId(TuitionClass c) => c.InstituteId ?? c.VenueId!.Value;

    private static ClassDto ToDto(TuitionClass c, PlaceNames places) =>
        new(c.Id, c.Title, c.SubjectId, c.StreamId, c.ExamYear, c.Medium, PlaceType(c), PlaceId(c), places.Name(c),
            c.InstituteId, c.MonthlyFee, c.Status.ToString(), c.ArchivedAt,
            [.. c.Slots.OrderBy(s => s.Day).ThenBy(s => s.Start).Select(s => new SlotDto(s.Id, s.Day, s.Start, s.End))]);

    /// <summary>Names of the institutes and venues classes point at. An archived class keeps its place even if it is later hidden.</summary>
    private sealed class PlaceNames(Dictionary<Guid, string> names)
    {
        public string Name(TuitionClass c) => names.GetValueOrDefault(PlaceId(c), "");

        public static async Task<PlaceNames> LoadAsync(ClassesDbContext db, CancellationToken ct)
        {
            var institutes = await db.Institutes.Select(i => new { i.Id, i.Name }).ToListAsync(ct);
            var venues = await db.Venues.Select(v => new { v.Id, v.Name }).ToListAsync(ct);
            return new PlaceNames(institutes.Concat(venues).ToDictionary(p => p.Id, p => p.Name));
        }
    }
}
