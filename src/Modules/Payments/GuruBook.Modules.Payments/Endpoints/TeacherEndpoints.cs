using GuruBook.Modules.Payments.Domain;
using GuruBook.Modules.Payments.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Payments.Endpoints;

public sealed record BankDetailsRequest(string? BankName, string? AccountName, string? AccountNumber, string? Branch, string? Note);

public sealed record BankDetailsDto(
    string BankName, string AccountName, string AccountNumber, string Branch, string Note, bool HasQr, long QrVersion);

/// <summary>One claim as the teacher sees it, with who it is from and what it is for.</summary>
public sealed record TeacherClaimDto(
    Guid Id, Guid EnrolmentId, Guid StudentId, int IndexNumber, string FullName, string ClassTitle,
    string Status, decimal Amount, decimal Outstanding, string Month, string Reference, string Note, bool HasSlip,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? RejectReason, string? ReceiptNo);

public sealed record ConfirmRequest(decimal? Amount);

public sealed record RejectRequest(string? Reason);

/// <summary>The teacher's side: where money should be sent, and the transfers waiting to be confirmed.</summary>
internal static class TeacherEndpoints
{
    private const int DecidedShown = 20;
    private const int MaxReasonLength = 200;

    public static void Map(RouteGroupBuilder group)
    {
        var teacher = group.MapGroup("")
            .RequireAuthorization(Policies.Teacher)
            .RequireFeature(PlanFeatures.BankPayments);

        teacher.MapGet("/details", Details);
        teacher.MapPut("/details", SaveDetails);
        teacher.MapGet("/qr", MyQr);
        teacher.MapPut("/qr", SaveQr);
        teacher.MapDelete("/qr", RemoveQr);
        teacher.MapGet("/claims", Claims);
        teacher.MapGet("/claims/{id:guid}/slip", Slip);
        teacher.MapPost("/claims/{id:guid}/confirm", Confirm);
        teacher.MapPost("/claims/{id:guid}/reject", Reject);
    }

    private static async Task<IResult> Details(PaymentsDbContext db, CancellationToken ct) =>
        Results.Ok(await ReadDetailsAsync(db, ct));

    private static async Task<IResult> SaveDetails(BankDetailsRequest request, PaymentsDbContext db, CancellationToken ct)
    {
        var bank = Problems.Clean(request.BankName);
        var name = Problems.Clean(request.AccountName);
        var number = Problems.Clean(request.AccountNumber);
        if (bank is null || name is null || number is null)
        {
            return Problems.Reject("details_incomplete");
        }

        if (bank.Length > 100 || name.Length > 100 || number.Length > 40
            || Problems.Clean(request.Branch)?.Length > 100 || Problems.Clean(request.Note)?.Length > 300)
        {
            return Problems.Reject("details_too_long");
        }

        var details = await db.BankDetails.FirstOrDefaultAsync(ct);
        if (details is null)
        {
            details = new BankDetails();
            db.BankDetails.Add(details);
        }

        details.BankName = bank;
        details.AccountName = name;
        details.AccountNumber = number;
        details.Branch = Problems.Clean(request.Branch) ?? "";
        details.Note = Problems.Clean(request.Note) ?? "";
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ReadDetailsAsync(db, ct));
    }

    // 204 rather than 404 when there is none, so a teacher who has not added a QR does not fill the browser console with errors.
    private static async Task<IResult> MyQr(PaymentsDbContext db, HttpContext http, CancellationToken ct)
    {
        var qr = await db.Qr.AsNoTracking().FirstOrDefaultAsync(ct);
        if (qr is null)
        {
            return Results.NoContent();
        }

        http.Response.Headers.CacheControl = "private, no-cache";
        return PayerEndpoints.Send(qr.Data, qr.ContentType, qr.UpdatedAt, http);
    }

    private static async Task<IResult> SaveQr(HttpRequest request, PaymentsDbContext db, CancellationToken ct)
    {
        if (request.ContentLength > SlipImage.MaxBytes)
        {
            return Problems.Reject("qr_too_large", StatusCodes.Status413PayloadTooLarge);
        }

        var bytes = await SlipImage.ReadLimitedAsync(request.Body, ct);
        if (bytes is null)
        {
            return Problems.Reject("qr_too_large", StatusCodes.Status413PayloadTooLarge);
        }

        if (SlipImage.TypeOf(bytes) is not { } type)
        {
            return Problems.Reject("qr_invalid");
        }

        var qr = await db.Qr.FirstOrDefaultAsync(ct);
        if (qr is null)
        {
            db.Qr.Add(new PayQr { ContentType = type, Data = bytes });
        }
        else
        {
            qr.ContentType = type;
            qr.Data = bytes;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await ReadDetailsAsync(db, ct));
    }

    private static async Task<IResult> RemoveQr(PaymentsDbContext db, CancellationToken ct)
    {
        await db.Qr.ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    // Waiting first, oldest first, then the latest decided ones so a teacher can see what they just did.
    private static async Task<IResult> Claims(PaymentsDbContext db, IFeeCollection fees, IClassRoster roster, IClassDirectory classes, CancellationToken ct)
    {
        var open = await db.Claims.AsNoTracking().Where(c => c.Status == ClaimStatus.Open).OrderBy(c => c.Id).ToListAsync(ct);
        var decided = await db.Claims.AsNoTracking().Where(c => c.Status != ClaimStatus.Open).OrderByDescending(c => c.Id).Take(DecidedShown).ToListAsync(ct);
        var rows = open.Concat(decided).ToList();

        var people = await roster.FindEnrolmentsAsync(rows.Select(c => c.EnrolmentId).Distinct(), ct);
        var list = new List<TeacherClaimDto>(rows.Count);
        foreach (var claim in rows)
        {
            var person = people.GetValueOrDefault(claim.EnrolmentId);
            var outstanding = claim.Status == ClaimStatus.Open
                ? (await fees.FindDueAsync(claim.EnrolmentId, claim.Month, ct))?.Outstanding ?? 0
                : 0;
            list.Add(new TeacherClaimDto(
                claim.Id, claim.EnrolmentId, claim.StudentId, person?.IndexNumber ?? 0, person?.FullName ?? "",
                (await classes.FindAsync(claim.ClassId, ct))?.Title ?? "", claim.Status.ToString(), claim.Amount, outstanding,
                claim.Month, claim.Reference, claim.Note, claim.HasSlip, claim.CreatedAt, claim.DecidedAt, claim.RejectReason, claim.ReceiptNo));
        }

        return Results.Ok(list);
    }

    private static async Task<IResult> Slip(Guid id, PaymentsDbContext db, HttpContext http, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null)
        {
            return Problems.Reject("claim_not_found", StatusCodes.Status404NotFound);
        }

        if (claim is not { Slip: { Length: > 0 } slip, SlipContentType: { } type })
        {
            return Results.NoContent();
        }

        http.Response.Headers.CacheControl = "private, no-cache";
        return PayerEndpoints.Send(slip, type, claim.UpdatedAt, http);
    }

    private static async Task<IResult> Confirm(Guid id, ConfirmRequest? request, ClaimService claims, CancellationToken ct)
    {
        var (claim, error, status) = await claims.ConfirmAsync(id, request?.Amount, ct);
        return claim is null ? Problems.Reject(error!, status) : Results.Ok(ClaimService.ToDto(claim));
    }

    private static async Task<IResult> Reject(Guid id, RejectRequest request, ClaimService claims, CancellationToken ct)
    {
        var reason = Problems.Clean(request.Reason);
        if (reason is { Length: > MaxReasonLength })
        {
            return Problems.Reject("reason_too_long");
        }

        var (claim, error, status) = await claims.RejectAsync(id, reason, ct);
        return claim is null ? Problems.Reject(error!, status) : Results.Ok(ClaimService.ToDto(claim));
    }

    private static async Task<BankDetailsDto> ReadDetailsAsync(PaymentsDbContext db, CancellationToken ct)
    {
        var details = await db.BankDetails.AsNoTracking().FirstOrDefaultAsync(ct);
        var qr = await db.Qr.AsNoTracking().Select(q => (DateTimeOffset?)q.UpdatedAt).FirstOrDefaultAsync(ct);
        return new BankDetailsDto(
            details?.BankName ?? "", details?.AccountName ?? "", details?.AccountNumber ?? "", details?.Branch ?? "",
            details?.Note ?? "", qr is not null, qr?.ToUnixTimeMilliseconds() ?? 0);
    }
}
