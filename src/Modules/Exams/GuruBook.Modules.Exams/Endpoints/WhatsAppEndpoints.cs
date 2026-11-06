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
        var whatsapp = group.MapGroup("/{id:guid}/whatsapp").RequireAuthorization(Policies.Teacher).RequireAnyFeature(PlanFeatures.WhatsApp, PlanFeatures.Sms);
        whatsapp.MapGet("", Preview);
        whatsapp.MapPost("", Send);
    }

    private static async Task<IResult> Preview(Guid id, ResultMessages results, IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await results.BuildAsync(id, ct);
        return plan is null ? Problems.Reject(error!, error == "exam_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict)
            : Results.Ok(WhatsAppPreviewDto.From(await sender.PreviewAsync(Drafts(plan), ct), plan.Names));
    }

    private static async Task<IResult> Send(Guid id, ResultMessages results, IWhatsAppSender sender, CancellationToken ct)
    {
        var (plan, error) = await results.BuildAsync(id, ct);
        if (plan is null)
        {
            return Problems.Reject(error!, error == "exam_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
        }

        var result = await sender.SendAsync(Drafts(plan), ct);
        return result.Configured ? Results.Ok(result) : Problems.Reject("whatsapp_not_configured", StatusCodes.Status409Conflict);
    }

    /// <summary>The same facts as the results that go out on their own, so a student is never sent an exam's results twice.</summary>
    private static List<WhatsAppDraft> Drafts(ResultMessages.Plan plan) =>
        [.. plan.Drafts.Select(d => new WhatsAppDraft(
            d.StudentId, WhatsAppKinds.Results, d.Params, d.DedupKey, d.Window, MessageEvents.ResultsPublished))];
}
