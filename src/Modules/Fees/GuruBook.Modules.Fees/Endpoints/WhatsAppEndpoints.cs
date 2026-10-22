using System.Globalization;
using GuruBook.Modules.Fees.Domain;
using GuruBook.Modules.Fees.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GuruBook.Modules.Fees.Endpoints;

/// <summary>
/// Fee reminders on WhatsApp (a Pro feature): one message to the parent of each student who still owes for the month, with what is left.
/// The same student is not reminded about the same month twice in a week. The teacher sees who would be reminded first, then confirms.
/// </summary>
internal static class WhatsAppEndpoints
{
    private static readonly TimeSpan ReminderEvery = TimeSpan.FromDays(7);

    public static void Map(RouteGroupBuilder group)
    {
        var whatsapp = group.MapGroup("/classes/{classId:guid}/whatsapp").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.WhatsApp);
        whatsapp.MapGet("", Preview);
        whatsapp.MapPost("", Send);
    }

    private sealed record Plan(List<WhatsAppDraft> Drafts, Dictionary<Guid, string> Names);

    private static async Task<IResult> Preview(
        Guid classId, string? month, ClassMonthService months, IClassRoster roster, IStudentContacts contacts, IClassDirectory classes,
        ITeacherDirectory teachers, ITenantContext user, IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await BuildAsync(classId, month, months, roster, contacts, classes, teachers, user, ct);
        return plan is null ? Problems.Reject(error!, error == "class_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest)
            : Results.Ok(WhatsAppPreviewDto.From(await sender.PreviewAsync(plan.Drafts, ct), plan.Names));
    }

    private static async Task<IResult> Send(
        Guid classId, string? month, ClassMonthService months, IClassRoster roster, IStudentContacts contacts, IClassDirectory classes,
        ITeacherDirectory teachers, ITenantContext user, IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await BuildAsync(classId, month, months, roster, contacts, classes, teachers, user, ct);
        if (plan is null)
        {
            return Problems.Reject(error!, error == "class_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        }

        var result = await sender.SendAsync(plan.Drafts, ct);
        return result.Configured ? Results.Ok(result) : Problems.Reject("whatsapp_not_configured", StatusCodes.Status409Conflict);
    }

    private static async Task<(Plan? Plan, string? Error)> BuildAsync(
        Guid classId, string? month, ClassMonthService months, IClassRoster roster, IStudentContacts contacts, IClassDirectory classes,
        ITeacherDirectory teachers, ITenantContext user, CancellationToken ct)
    {
        var result = await months.BuildAsync(classId, month, ct);
        if (result.Value is not { } value)
        {
            return (null, result.Error);
        }

        var owing = value.Rows.Where(r => r.Status is nameof(MonthStatus.Unpaid) or nameof(MonthStatus.PartlyPaid)).ToList();
        var people = await roster.FindEnrolmentsAsync(owing.Select(r => r.EnrolmentId), ct);
        var found = await contacts.FindAsync(people.Values.Select(p => p.StudentId), ct);
        var title = (await classes.FindAsync(classId, ct))?.Title ?? "";
        var teacher = user.TenantId is { } tenantId ? await teachers.FindNameAsync(tenantId, ct) ?? "" : "";
        var monthLabel = DateOnly.ParseExact(value.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        var drafts = new List<WhatsAppDraft>();
        var names = new Dictionary<Guid, string>();
        foreach (var row in owing)
        {
            var person = people[row.EnrolmentId];
            var first = found.TryGetValue(person.StudentId, out var contact) ? contact.FirstName : person.FullName;
            names[person.StudentId] = person.FullName;
            drafts.Add(new WhatsAppDraft(
                person.StudentId, WhatsAppKinds.FeeReminder, [first, title, monthLabel, Lkr(row.Due - row.Paid), teacher],
                $"fee:{row.EnrolmentId}:{value.Month}", ReminderEvery));
        }

        return (new Plan(drafts, names), null);
    }

    private static string Lkr(decimal amount) =>
        "LKR " + amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", CultureInfo.InvariantCulture);
}
