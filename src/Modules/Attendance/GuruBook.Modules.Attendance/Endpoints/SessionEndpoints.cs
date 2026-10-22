using GuruBook.Modules.Attendance.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Attendance.Endpoints;

public sealed record AddSessionRequest(Guid? ClassId, DateOnly? Date, string? Start, string? End, string? Note);

public sealed record MarkRequest(Guid? EnrolmentId, string? Status);

public sealed record SaveMarksRequest(MarkRequest[]? Marks);

public sealed record SessionDto(
    Guid Id, Guid ClassId, string ClassTitle, DateOnly Date, string Start, string End, bool IsExtra, string? Note,
    bool Taken, int Present, int Late, int Absent, bool Editable);

public sealed record SessionStudentDto(Guid EnrolmentId, int IndexNumber, string FullName, string Status);

public sealed record SessionDetailDto(SessionDto Session, SessionStudentDto[] Students);

public sealed record ScanRequest(string? Code);

/// <summary>The student a scanned card belongs to, as they appear on this session's list.</summary>
public sealed record ScanDto(Guid EnrolmentId, int IndexNumber, string FullName);

/// <summary>
/// Sessions of a class and the marks for each. Everyone starts as present: the teacher only taps the students who are
/// late or absent, then saves. A day's sessions can be marked or corrected for a week afterwards.
/// </summary>
internal static class SessionEndpoints
{
    public const int EditWindowDays = 7;

    /// <summary>What a printed card's QR text starts with; the Students module writes it when it makes the sheet.</summary>
    public const string CardPrefix = "EV1:";

    public static void Map(RouteGroupBuilder group)
    {
        var sessions = group.MapGroup("/sessions").RequireAuthorization(Policies.Teacher);
        sessions.MapGet("", List);
        sessions.MapPost("", AddExtra);
        sessions.MapGet("/{id:guid}", Get);
        sessions.MapPut("/{id:guid}/marks", SaveMarks);
        sessions.MapPost("/{id:guid}/scan", Scan);
        sessions.MapDelete("/{id:guid}", Delete);
    }

    public static bool CanEdit(DateOnly date, DateOnly today) => date <= today && date >= today.AddDays(-EditWindowDays);

    // The sessions of one day. A day inside the edit window gets its sessions made from the class timetable the first time it is opened.
    private static async Task<IResult> List(
        DateOnly? date, AttendanceDbContext db, IClassDirectory classes, TimeProvider clock, CancellationToken ct)
    {
        var today = Problems.Today(clock);
        var day = date ?? today;
        if (CanEdit(day, today))
        {
            await EnsureGeneratedAsync(db, classes, day, ct);
        }

        var rows = await db.Sessions.Where(s => s.Date == day).ToListAsync(ct);
        var dtos = await ToDtosAsync(rows, db, classes, today, ct);
        return Results.Ok(dtos.OrderBy(d => d.Start).ThenBy(d => d.ClassTitle).ToList());
    }

    private static async Task EnsureGeneratedAsync(AttendanceDbContext db, IClassDirectory classes, DateOnly day, CancellationToken ct)
    {
        var meetings = await classes.MeetingsOnAsync(day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek, ct);
        if (meetings.Count == 0)
        {
            return;
        }

        var existing = await db.Sessions.Where(s => s.Date == day).Select(s => new { s.ClassId, s.Start }).ToListAsync(ct);
        var missing = meetings.Where(m => !existing.Any(e => e.ClassId == m.ClassId && e.Start == m.Start)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var meeting in missing)
        {
            db.Sessions.Add(new ClassSession { ClassId = meeting.ClassId, Date = day, Start = meeting.Start, End = meeting.End });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request made the same sessions a moment ago; the unique index stopped the duplicates and we simply read theirs.
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<IResult> AddExtra(
        AddSessionRequest request, AttendanceDbContext db, IClassDirectory classes, IClassRoster roster, INotifier notifier, TimeProvider clock, CancellationToken ct)
    {
        var today = Problems.Today(clock);
        if (request.Date is not { } date)
        {
            return Problems.Reject("date_required");
        }

        if (!CanEdit(date, today))
        {
            return Problems.Reject(date > today ? "date_in_future" : "outside_edit_window");
        }

        if (!TimeOnly.TryParse(request.Start, out var start) || !TimeOnly.TryParse(request.End, out var end) || end <= start)
        {
            return Problems.Reject("time_range_invalid");
        }

        var note = Problems.Clean(request.Note);
        if (note is { Length: > 200 })
        {
            return Problems.Reject("note_too_long");
        }

        var cls = request.ClassId is { } id ? await classes.FindAsync(id, ct) : null;
        if (cls is null || cls.IsArchived)
        {
            return Problems.Reject("class_not_found", StatusCodes.Status404NotFound);
        }

        if (await db.Sessions.AnyAsync(s => s.ClassId == cls.Id && s.Date == date && s.Start == start, ct))
        {
            return Problems.Reject("session_exists", StatusCodes.Status409Conflict);
        }

        var session = new ClassSession { ClassId = cls.Id, Date = date, Start = start, End = end, IsExtra = true, Note = note };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        // An extra class can only be added for today or earlier, so the only one worth telling students about is one still to come today.
        var now = TimeOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);
        if (date == today && end > now)
        {
            var students = await roster.ActiveRosterAsync(cls.Id, ct);
            await notifier.NotifyAsync(
                [.. students.Where(s => s.UserId is not null).Select(s => new NotifyRequest(
                    s.UserId!.Value, "extra_class",
                    new { classTitle = cls.Title, start = start.ToString("HH:mm"), end = end.ToString("HH:mm"), note },
                    Link: "/welcome", OnceKey: $"extra:{session.Id}"))],
                ct);
        }

        var dto = (await ToDtosAsync([session], db, classes, today, ct))[0];
        return Results.Created($"/api/attendance/sessions/{session.Id}", dto);
    }

    private static async Task<IResult> Get(
        Guid id, AttendanceDbContext db, IClassDirectory classes, IClassRoster roster, TimeProvider clock, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([id], ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        var today = Problems.Today(clock);
        var dto = (await ToDtosAsync([session], db, classes, today, ct))[0];
        var marks = await db.Marks.Where(m => m.SessionId == id).ToDictionaryAsync(m => m.EnrolmentId, m => m.Status, ct);

        // The class list today, plus anyone who has marks here but has since left, so history stays complete.
        var students = (await roster.ActiveRosterAsync(session.ClassId, ct)).ToDictionary(r => r.EnrolmentId);
        var leftIds = marks.Keys.Where(k => !students.ContainsKey(k)).ToList();
        if (leftIds.Count > 0)
        {
            foreach (var (enrolmentId, entry) in await roster.FindEnrolmentsAsync(leftIds, ct))
            {
                students[enrolmentId] = entry;
            }
        }

        var rows = students.Values
            .OrderBy(s => s.IndexNumber)
            .Select(s => new SessionStudentDto(
                s.EnrolmentId, s.IndexNumber, s.FullName, (marks.TryGetValue(s.EnrolmentId, out var status) ? status : AttendanceStatus.Present).ToString()))
            .ToArray();
        return Results.Ok(new SessionDetailDto(dto, rows));
    }

    // The request lists the students who are not present; anyone not listed is present. Saving again replaces the earlier marks.
    private static async Task<IResult> SaveMarks(
        Guid id, SaveMarksRequest request, AttendanceDbContext db, IClassDirectory classes, IClassRoster roster,
        TimeProvider clock, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([id], ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        var today = Problems.Today(clock);
        if (!CanEdit(session.Date, today))
        {
            return Problems.Reject(session.Date > today ? "date_in_future" : "outside_edit_window");
        }

        var wanted = new Dictionary<Guid, AttendanceStatus>();
        foreach (var mark in request.Marks ?? [])
        {
            if (mark.EnrolmentId is not { } enrolmentId || !Enum.TryParse<AttendanceStatus>(mark.Status, ignoreCase: true, out var status))
            {
                return Problems.Reject("mark_invalid");
            }

            wanted[enrolmentId] = status;
        }

        var students = (await roster.ActiveRosterAsync(session.ClassId, ct)).Select(r => r.EnrolmentId).ToHashSet();
        var existing = await db.Marks.Where(m => m.SessionId == id).ToDictionaryAsync(m => m.EnrolmentId, ct);
        if (wanted.Keys.Any(k => !students.Contains(k) && !existing.ContainsKey(k)))
        {
            return Problems.Reject("student_not_in_class");
        }

        // Students who have left keep what was recorded; everyone on the class list is saved, present by default.
        foreach (var enrolmentId in students)
        {
            var status = wanted.TryGetValue(enrolmentId, out var chosen) ? chosen : AttendanceStatus.Present;
            if (existing.TryGetValue(enrolmentId, out var mark))
            {
                mark.Status = status;
            }
            else
            {
                db.Marks.Add(new AttendanceMark { SessionId = id, EnrolmentId = enrolmentId, Status = status });
            }
        }

        foreach (var (enrolmentId, status) in wanted.Where(w => !students.Contains(w.Key)))
        {
            existing[enrolmentId].Status = status;
        }

        session.TakenAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var dto = (await ToDtosAsync([session], db, classes, today, ct))[0];
        return Results.Ok(dto);
    }

    // Looks up a scanned student card. Nothing is saved: the screen collects who has arrived, and the teacher saves the marks as usual.
    private static async Task<IResult> Scan(
        Guid id, ScanRequest request, AttendanceDbContext db, IClassRoster roster, IStudentCards cards, TimeProvider clock, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([id], ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        if (!CanEdit(session.Date, Problems.Today(clock)))
        {
            return Problems.Reject(session.Date > Problems.Today(clock) ? "date_in_future" : "outside_edit_window");
        }

        var code = request.Code?.Trim() ?? "";
        if (!code.StartsWith(CardPrefix, StringComparison.Ordinal) || code.Length > 64
            || await cards.StudentOfAsync(code[CardPrefix.Length..], ct) is not { } studentId)
        {
            return Problems.Reject("card_unknown", StatusCodes.Status404NotFound);
        }

        var entry = (await roster.ActiveRosterAsync(session.ClassId, ct)).FirstOrDefault(r => r.StudentId == studentId);
        return entry is null
            ? Problems.Reject("card_not_in_class", StatusCodes.Status409Conflict)
            : Results.Ok(new ScanDto(entry.EnrolmentId, entry.IndexNumber, entry.FullName));
    }

    // Only an extra class nobody has marked yet can be removed; anything else is history.
    private static async Task<IResult> Delete(Guid id, AttendanceDbContext db, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([id], ct);
        if (session is null)
        {
            return Results.NotFound();
        }

        if (!session.IsExtra || session.TakenAt is not null)
        {
            return Problems.Reject("session_not_removable", StatusCodes.Status409Conflict);
        }

        db.Sessions.Remove(session);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<List<SessionDto>> ToDtosAsync(
        List<ClassSession> sessions, AttendanceDbContext db, IClassDirectory classes, DateOnly today, CancellationToken ct)
    {
        var titles = await classes.FindManyAsync(sessions.Select(s => s.ClassId), ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var counts = (await db.Marks.Where(m => ids.Contains(m.SessionId)).Select(m => new { m.SessionId, m.Status }).ToListAsync(ct))
            .GroupBy(m => m.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return [.. sessions.Select(s =>
        {
            var marks = counts.TryGetValue(s.Id, out var found) ? found : [];
            return new SessionDto(
                s.Id, s.ClassId, titles.TryGetValue(s.ClassId, out var cls) ? cls.Title : "", s.Date,
                s.Start.ToString("HH:mm"), s.End.ToString("HH:mm"), s.IsExtra, s.Note, s.TakenAt is not null,
                marks.Count(m => m.Status == AttendanceStatus.Present), marks.Count(m => m.Status == AttendanceStatus.Late),
                marks.Count(m => m.Status == AttendanceStatus.Absent), CanEdit(s.Date, today));
        })];
    }
}
