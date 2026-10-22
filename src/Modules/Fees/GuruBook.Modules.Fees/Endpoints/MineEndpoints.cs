using GuruBook.Modules.Fees.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Endpoints;

public sealed record MyReceiptDto(string ReceiptNo, decimal Amount, string Method, DateOnly PaidOn);

public sealed record MyFeesDto(Guid ClassId, decimal Outstanding, DueDto[] Months, MyReceiptDto[] Receipts);

/// <summary>
/// A student's own fee status in each class they are in: the last few months and their latest receipts. A parent gets the same for one
/// child, through the links their child's teachers made.
/// </summary>
internal static class MineEndpoints
{
    private const int MonthsShown = 6;
    private const int ReceiptsShown = 5;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/mine", Mine).RequireAuthorization(Policies.Student);
        group.MapGet("/children/{studentId:guid}", Child).RequireAuthorization(Policies.Parent);
    }

    private static async Task<IResult> Mine(FeesDbContext db, IClassRoster roster, ITenantContext user, TimeProvider clock, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await Load(db, await roster.ActiveEnrolmentsOfUserAsync(userId, ct), clock, ct));
    }

    private static async Task<IResult> Child(Guid studentId, FeesDbContext db, IClassRoster roster, ITenantContext user, TimeProvider clock, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await Load(db, await roster.ActiveEnrolmentsOfChildAsync(userId, studentId, ct), clock, ct));
    }

    private static async Task<List<MyFeesDto>> Load(FeesDbContext db, IReadOnlyList<MyEnrolment> enrolments, TimeProvider clock, CancellationToken ct)
    {
        var current = FeeMonths.Current(clock);
        var ids = enrolments.Select(e => e.EnrolmentId).ToList();

        // The reader is not a teacher, so the teacher filter is lifted and the enrolments handed in are the only way in.
        var dues = await db.Dues.IgnoreQueryFilters().Where(d => ids.Contains(d.EnrolmentId)).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. dues.Select(d => d.Id)], ct);
        var payments = await db.Payments.IgnoreQueryFilters().Where(p => ids.Contains(p.EnrolmentId) && p.VoidedAt == null).ToListAsync(ct);

        var result = enrolments.Select(e =>
        {
            var months = dues.Where(d => d.EnrolmentId == e.EnrolmentId && string.CompareOrdinal(d.Month, current) <= 0)
                .OrderByDescending(d => d.Month)
                .Select(d => new DueDto(d.Month, d.AmountDue, paid.GetValueOrDefault(d.Id), FeeMonths.StatusOf(d.AmountDue, paid.GetValueOrDefault(d.Id)).ToString()))
                .ToList();
            var receipts = payments.Where(p => p.EnrolmentId == e.EnrolmentId)
                .OrderByDescending(p => p.ReceiptNo).Take(ReceiptsShown)
                .Select(p => new MyReceiptDto(p.ReceiptNo, p.Amount, p.Method.ToString(), p.PaidOn));
            return new MyFeesDto(e.ClassId, months.Sum(m => m.Due - m.Paid), [.. months.Take(MonthsShown)], [.. receipts]);
        }).ToList();
        return result;
    }
}
