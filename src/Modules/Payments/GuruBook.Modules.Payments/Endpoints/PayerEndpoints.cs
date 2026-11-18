using GuruBook.Modules.Payments.Domain;
using GuruBook.Modules.Payments.Services;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace GuruBook.Modules.Payments.Endpoints;

public sealed record ClaimRequest(string? Month, decimal? Amount, string? Note);

/// <summary>
/// The paying side: a student for their own fee, a parent for a child a teacher linked to them. Every route starts from an enrolment and
/// checks with the roster that it is theirs, so naming someone else's fee gets a 404 rather than their teacher's bank details.
/// </summary>
internal static class PayerEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var mine = group.MapGroup("/mine").RequireAuthorization(Policies.Student);
        mine.MapGet("", (ClaimService claims, CancellationToken ct) => claims.PayableAsync(null, ct));
        mine.MapGet("/{enrolmentId:guid}", (Guid enrolmentId, string? month, ClaimService claims, CancellationToken ct) =>
            How(null, enrolmentId, month, claims, ct));
        mine.MapGet("/{enrolmentId:guid}/qr", (Guid enrolmentId, ClaimService claims, PaymentsDbContext db, HttpContext http, CancellationToken ct) =>
            Qr(null, enrolmentId, claims, db, http, ct));
        mine.MapPost("/{enrolmentId:guid}/claims", (Guid enrolmentId, ClaimRequest request, ClaimService claims, CancellationToken ct) =>
            Claim(null, enrolmentId, request, byParent: false, claims, ct));

        var child = group.MapGroup("/children/{studentId:guid}").RequireAuthorization(Policies.Parent);
        child.MapGet("", (Guid studentId, ClaimService claims, CancellationToken ct) => claims.PayableAsync(studentId, ct));
        child.MapGet("/{enrolmentId:guid}", (Guid studentId, Guid enrolmentId, string? month, ClaimService claims, CancellationToken ct) =>
            How(studentId, enrolmentId, month, claims, ct));
        child.MapGet("/{enrolmentId:guid}/qr", (Guid studentId, Guid enrolmentId, ClaimService claims, PaymentsDbContext db, HttpContext http, CancellationToken ct) =>
            Qr(studentId, enrolmentId, claims, db, http, ct));
        child.MapPost("/{enrolmentId:guid}/claims", (Guid studentId, Guid enrolmentId, ClaimRequest request, ClaimService claims, CancellationToken ct) =>
            Claim(studentId, enrolmentId, request, byParent: true, claims, ct));

        // The slip rides on the claim that was just made, so one route serves both: it is the claim's own author who may add it.
        group.MapPut("/claims/{id:guid}/slip", Slip).RequireAuthorization(Policies.Active).RequireRateLimiting(RateLimits.Uploads);
    }

    private static async Task<IResult> How(Guid? studentId, Guid enrolmentId, string? month, ClaimService claims, CancellationToken ct)
    {
        if (await claims.FindPayerEnrolmentAsync(studentId, enrolmentId, ct) is not { } enrolment)
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        return await claims.HowAsync(enrolment, month ?? "", ct) is { } way
            ? Results.Ok(way)
            : Problems.Reject("fee_not_found", StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Claim(
        Guid? studentId, Guid enrolmentId, ClaimRequest request, bool byParent, ClaimService claims, CancellationToken ct)
    {
        if (await claims.FindPayerEnrolmentAsync(studentId, enrolmentId, ct) is not { } enrolment)
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        var (claim, error, status) = await claims.CreateAsync(
            enrolment, request.Month ?? "", request.Amount ?? 0, Problems.Clean(request.Note), byParent, ct);
        return claim is null
            ? Problems.Reject(error!, status)
            : Results.Created($"/api/payments/claims/{claim.Id}", ClaimService.ToDto(claim));
    }

    // The teacher's QR, for a payer who is in one of that teacher's classes. 204 when there is none, so the app can simply not show it.
    private static async Task<IResult> Qr(
        Guid? studentId, Guid enrolmentId, ClaimService claims, PaymentsDbContext db, HttpContext http, CancellationToken ct)
    {
        if (await claims.FindPayerEnrolmentAsync(studentId, enrolmentId, ct) is not { } enrolment)
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        var qr = await db.Qr.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(q => q.TenantId == enrolment.TenantId, ct);
        if (qr is null)
        {
            return Results.NoContent();
        }

        http.Response.Headers.CacheControl = "private, max-age=86400";
        return Send(qr.Data, qr.ContentType, qr.UpdatedAt, http);
    }

    // The slip photo of a claim, added right after it is made. Only its author, and only while it is waiting.
    private static async Task<IResult> Slip(Guid id, HttpRequest request, PaymentsDbContext db, ITenantContext user, CancellationToken ct)
    {
        var claim = await db.Claims.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null || user.UserId is not { } userId || claim.ClaimedBy != userId)
        {
            return Problems.Reject("claim_not_found", StatusCodes.Status404NotFound);
        }

        if (claim.Status != ClaimStatus.Open)
        {
            return Problems.Reject("claim_decided", StatusCodes.Status409Conflict);
        }

        if (request.ContentLength > SlipImage.MaxBytes)
        {
            return Problems.Reject("slip_too_large", StatusCodes.Status413PayloadTooLarge);
        }

        var bytes = await SlipImage.ReadLimitedAsync(request.Body, ct);
        if (bytes is null)
        {
            return Problems.Reject("slip_too_large", StatusCodes.Status413PayloadTooLarge);
        }

        if (SlipImage.TypeOf(bytes) is not { } type)
        {
            return Problems.Reject("slip_invalid");
        }

        claim.SlipContentType = type;
        claim.Slip = bytes;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ClaimService.ToDto(claim));
    }

    internal static IResult Send(byte[] data, string contentType, DateTimeOffset updatedAt, HttpContext http)
    {
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(
            data, contentType, lastModified: updatedAt,
            entityTag: new EntityTagHeaderValue($"\"{updatedAt.ToUnixTimeMilliseconds()}\""));
    }
}
