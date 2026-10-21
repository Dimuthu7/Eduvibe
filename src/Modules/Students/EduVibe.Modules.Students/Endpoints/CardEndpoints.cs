using System.Security.Cryptography;
using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students.Endpoints;

/// <summary>What is printed on a card: the QR text is <see cref="CardEndpoints.Prefix"/> followed by the code.</summary>
public sealed record CardDto(Guid StudentId, string FullName, int? IndexNumber, string Code);

/// <summary>
/// Student cards for QR attendance. A card is made the first time it is asked for, so teachers never "set up" anything: they open the
/// class, print the sheet, hand the cards out. A lost card is replaced, which makes the old one useless.
/// </summary>
internal static class CardEndpoints
{
    /// <summary>Marks the text as an EduVibe card, so a scanner that reads some other QR code can say so instead of looking it up.</summary>
    public const string Prefix = "EV1:"; // the Attendance module reads the same text; see SessionEndpoints.CardPrefix

    public static void Map(RouteGroupBuilder group)
    {
        var teacher = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        teacher.MapGet("/cards", ForClass);
        teacher.MapGet("/{id:guid}/card", ForStudent);
        teacher.MapPost("/{id:guid}/card/replace", Replace);
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
