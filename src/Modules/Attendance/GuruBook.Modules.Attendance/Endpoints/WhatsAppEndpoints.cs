using System.Globalization;
using GuruBook.Modules.Attendance.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Attendance.Endpoints;

/// <summary>
/// Absence alerts on WhatsApp (a Pro feature): after attendance is saved, the teacher can tell the parent of each student marked absent.
/// A student is told once per class session. Late students are not included: they came.
/// </summary>
internal static class WhatsAppEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var whatsapp = group.MapGroup("/sessions/{id:guid}/whatsapp").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.WhatsApp);
        whatsapp.MapGet("", Preview);
        whatsapp.MapPost("", Send);
    }

    private sealed record Plan(List<WhatsAppDraft> Drafts, Dictionary<Guid, string> Names);

    private static async Task<IResult> Preview(
        Guid id, AttendanceDbContext db, IClassRoster roster, IStudentContacts contacts, IClassDirectory classes, ITeacherDirectory teachers,
        ITenantContext user, IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await BuildAsync(id, db, roster, contacts, classes, teachers, user, ct);
        return plan is null ? Problems.Reject(error!, error == "session_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict)
            : Results.Ok(WhatsAppPreviewDto.From(await sender.PreviewAsync(plan.Drafts, ct), plan.Names));
    }

    private static async Task<IResult> Send(
        Guid id, AttendanceDbContext db, IClassRoster roster, IStudentContacts contacts, IClassDirectory classes, ITeacherDirectory teachers,
        ITenantContext user, IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await BuildAsync(id, db, roster, contacts, classes, teachers, user, ct);
        if (plan is null)
        {
            return Problems.Reject(error!, error == "session_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
        }

        var result = await sender.SendAsync(plan.Drafts, ct);
        return result.Configured ? Results.Ok(result) : Problems.Reject("whatsapp_not_configured", StatusCodes.Status409Conflict);
    }

    private static async Task<(Plan? Plan, string? Error)> BuildAsync(
        Guid sessionId, AttendanceDbContext db, IClassRoster roster, IStudentContacts contacts, IClassDirectory classes, ITeacherDirectory teachers,
        ITenantContext user, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([sessionId], ct);
        if (session is null)
        {
            return (null, "session_not_found");
        }

        if (session.TakenAt is null)
        {
            return (null, "attendance_not_taken");
        }

        var absent = await db.Marks.Where(m => m.SessionId == sessionId && m.Status == AttendanceStatus.Absent).Select(m => m.EnrolmentId).ToListAsync(ct);
        var people = await roster.FindEnrolmentsAsync(absent, ct);
        var found = await contacts.FindAsync(people.Values.Select(p => p.StudentId), ct);
        var title = (await classes.FindAsync(session.ClassId, ct))?.Title ?? "";
        var teacher = user.TenantId is { } tenantId ? await teachers.FindNameAsync(tenantId, ct) ?? "" : "";
        var date = session.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        var drafts = new List<WhatsAppDraft>();
        var names = new Dictionary<Guid, string>();
        foreach (var person in people.Values.OrderBy(p => p.IndexNumber))
        {
            var enrolmentId = person.EnrolmentId;
            names[person.StudentId] = person.FullName;
            var first = found.TryGetValue(person.StudentId, out var contact) ? contact.FirstName : person.FullName;
            drafts.Add(new WhatsAppDraft(person.StudentId, WhatsAppKinds.Absence, [first, title, date, teacher], $"absent:{sessionId}:{enrolmentId}"));
        }

        return (new Plan(drafts, names), null);
    }
}
