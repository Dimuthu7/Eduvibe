using System.Globalization;
using GuruBook.Modules.Exams.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GuruBook.Modules.Exams.Endpoints;

/// <summary>
/// Results on WhatsApp (a Pro feature): once an exam is published, the teacher can send each student's parent their marks, grade and rank.
/// Absent students and students with marks still missing are left out, and a student is sent an exam's results once.
/// </summary>
internal static class WhatsAppEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var whatsapp = group.MapGroup("/{id:guid}/whatsapp").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.WhatsApp);
        whatsapp.MapGet("", Preview);
        whatsapp.MapPost("", Send);
    }

    private sealed record Plan(List<WhatsAppDraft> Drafts, Dictionary<Guid, string> Names);

    private static async Task<IResult> Preview(
        Guid id, ExamReader reader, IClassRoster roster, IStudentContacts contacts, ITeacherDirectory teachers, ITenantContext user,
        IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await BuildAsync(id, reader, roster, contacts, teachers, user, ct);
        return plan is null ? Problems.Reject(error!, error == "exam_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict)
            : Results.Ok(WhatsAppPreviewDto.From(await sender.PreviewAsync(plan.Drafts, ct), plan.Names));
    }

    private static async Task<IResult> Send(
        Guid id, ExamReader reader, IClassRoster roster, IStudentContacts contacts, ITeacherDirectory teachers, ITenantContext user,
        IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await BuildAsync(id, reader, roster, contacts, teachers, user, ct);
        if (plan is null)
        {
            return Problems.Reject(error!, error == "exam_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
        }

        var result = await sender.SendAsync(plan.Drafts, ct);
        return result.Configured ? Results.Ok(result) : Problems.Reject("whatsapp_not_configured", StatusCodes.Status409Conflict);
    }

    private static async Task<(Plan? Plan, string? Error)> BuildAsync(
        Guid examId, ExamReader reader, IClassRoster roster, IStudentContacts contacts, ITeacherDirectory teachers, ITenantContext user, CancellationToken ct)
    {
        var exam = await reader.DetailAsync(examId, ct);
        if (exam is null)
        {
            return (null, "exam_not_found");
        }

        if (exam.PublishedAt is null)
        {
            return (null, "exam_not_published");
        }

        var results = exam.Students.Where(s => s.IsActive && !s.Absent && s.Total is not null).ToList();
        var ranked = exam.Students.Count(s => s.Rank is not null);
        var people = await roster.FindEnrolmentsAsync(results.Select(r => r.EnrolmentId), ct);
        var found = await contacts.FindAsync(people.Values.Select(p => p.StudentId), ct);
        var teacher = user.TenantId is { } tenantId ? await teachers.FindNameAsync(tenantId, ct) ?? "" : "";

        var drafts = new List<WhatsAppDraft>();
        var names = new Dictionary<Guid, string>();
        foreach (var result in results.OrderBy(r => r.IndexNumber))
        {
            var person = people[result.EnrolmentId];
            names[person.StudentId] = person.FullName;
            var first = found.TryGetValue(person.StudentId, out var contact) ? contact.FirstName : person.FullName;
            drafts.Add(new WhatsAppDraft(
                person.StudentId, WhatsAppKinds.Results,
                [first, exam.Name, exam.ClassTitle, $"{Mark(result.Total!.Value)} / {Mark(exam.MaxTotal)}", result.Grade ?? "-", result.Rank is { } rank ? $"{rank} of {ranked}" : "-", teacher],
                $"result:{examId}:{person.StudentId}"));
        }

        return (new Plan(drafts, names), null);
    }

    private static string Mark(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
