using System.Security.Cryptography;
using GuruBook.Modules.Students.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students.Endpoints;

/// <summary>What is printed on a card: the QR text is <see cref="CardEndpoints.Prefix"/> followed by the code.</summary>
public sealed record CardDto(Guid StudentId, string FullName, int? IndexNumber, string Code);

/// <summary>The text read off a card, with or without the <see cref="CardEndpoints.Prefix"/>.</summary>
public sealed record FindCardRequest(string? Code);

/// <summary>
/// Student cards for QR attendance. A card is made the first time it is asked for, so teachers never "set up" anything: they open the
/// class, print the sheet, hand the cards out. A lost card is replaced, which makes the old one useless.
/// </summary>
internal static class CardEndpoints
{
    /// <summary>Marks the text as an GuruBook card, so a scanner that reads some other QR code can say so instead of looking it up.</summary>
    public const string Prefix = "EV1:"; // the Attendance module reads the same text; see SessionEndpoints.CardPrefix

    public static void Map(RouteGroupBuilder group)
    {
        var teacher = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        teacher.MapGet("/cards", ForClass);
        teacher.MapPost("/cards/find", Find);
        teacher.MapGet("/{id:guid}/card", ForStudent);
        teacher.MapPost("/{id:guid}/card/replace", Replace);
    }

    // The student a scanned card belongs to. Nothing is saved: the teacher is looking a student up, not marking anything.
    private static async Task<IResult> Find(
        FindCardRequest request, StudentsDbContext db, IClassDirectory classes, ITenantContext tenant, CancellationToken ct)
    {
        var text = request.Code?.Trim() ?? "";
        var code = text.StartsWith(Prefix, StringComparison.Ordinal) ? text[Prefix.Length..] : text;
        if (code.Length is 0 or > 64)
        {
            return Problems.Reject("card_unknown", StatusCodes.Status404NotFound);
        }

        // Only this teacher's own cards are found, because the query filter keeps another teacher's rows out.
        var studentId = await db.StudentCards.Where(c => c.Code == code).Select(c => (Guid?)c.StudentId).FirstOrDefaultAsync(ct);
        if (studentId is not { } id)
        {
            return Problems.Reject("card_unknown", StatusCodes.Status404NotFound);
        }

        var enrolments = await db.Enrolments.Include(e => e.Student).Where(e => e.StudentId == id).ToListAsync(ct);
        if (enrolments.Count == 0)
        {
            return Problems.Reject("card_unknown", StatusCodes.Status404NotFound);
        }

        var titles = await classes.FindManyAsync(enrolments.Select(e => e.ClassId), ct);
        var hasParent = await db.ParentLinks.AnyAsync(l => l.StudentId == id, ct);
        var manages = await StudentEndpoints.AccountTeacherAsync(db, id, ct) == tenant.TenantId;
        return Results.Ok(StudentEndpoints.ToStudent(enrolments[0].Student, enrolments, titles, hasParent, manages));
    }

    internal static string NewCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Every active student of a class, with a card each (made now for anyone who has none).
    private static async Task<IResult> ForClass(Guid? classId, StudentsDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        if (classId is not { } id || tenant.TenantId is not { } tenantId)
        {
            return Problems.Reject("class_required");
        }

        var rows = await db.Enrolments.Include(e => e.Student)
            .Where(e => e.ClassId == id && e.Status == EnrolmentStatus.Active)
            .OrderBy(e => e.IndexNumber)
            .ToListAsync(ct);
        var studentIds = rows.Select(r => r.StudentId).ToList();
        var cards = await db.StudentCards.Where(c => studentIds.Contains(c.StudentId)).ToDictionaryAsync(c => c.StudentId, ct);
        foreach (var row in rows.Where(r => !cards.ContainsKey(r.StudentId)))
        {
            var card = new StudentCard { TenantId = tenantId, StudentId = row.StudentId, Code = NewCode() };
            db.StudentCards.Add(card);
            cards[row.StudentId] = card;
        }

        if (await StudentEndpoints.SaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        return Results.Ok(rows.Select(r => new CardDto(r.StudentId, r.Student.FullName, r.IndexNumber, cards[r.StudentId].Code)).ToList());
    }

    private static async Task<IResult> ForStudent(Guid id, Guid? classId, StudentsDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var enrolments = await db.Enrolments.Include(e => e.Student).Where(e => e.StudentId == id).ToListAsync(ct);
        if (enrolments.Count == 0 || tenant.TenantId is not { } tenantId)
        {
            return Results.NotFound();
        }

        var card = await db.StudentCards.FirstOrDefaultAsync(c => c.StudentId == id, ct);
        if (card is null)
        {
            card = new StudentCard { TenantId = tenantId, StudentId = id, Code = NewCode() };
            db.StudentCards.Add(card);
            if (await StudentEndpoints.SaveAsync(db, ct) is { } conflict)
            {
                return conflict;
            }
        }

        var enrolment = enrolments.FirstOrDefault(e => e.ClassId == classId) ?? enrolments.OrderByDescending(e => e.Status == EnrolmentStatus.Active).First();
        return Results.Ok(new CardDto(id, enrolment.Student.FullName, classId is null ? null : enrolment.IndexNumber, card.Code));
    }

    private static async Task<IResult> Replace(Guid id, StudentsDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        var enrolment = await db.Enrolments.Include(e => e.Student).FirstOrDefaultAsync(e => e.StudentId == id, ct);
        if (enrolment is null || tenant.TenantId is not { } tenantId)
        {
            return Results.NotFound();
        }

        // A student who has no card yet simply gets one; either way the old code, if there was one, stops working.
        var card = await db.StudentCards.FirstOrDefaultAsync(c => c.StudentId == id, ct);
        if (card is null)
        {
            card = new StudentCard { TenantId = tenantId, StudentId = id, Code = NewCode() };
            db.StudentCards.Add(card);
        }
        else
        {
            card.Code = NewCode();
        }

        if (await StudentEndpoints.SaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        return Results.Ok(new CardDto(id, enrolment.Student.FullName, null, card.Code));
    }
}
