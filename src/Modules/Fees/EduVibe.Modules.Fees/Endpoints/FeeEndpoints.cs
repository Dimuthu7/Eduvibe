using EduVibe.Modules.Fees.Domain;
using EduVibe.Modules.Fees.Services;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Fees.Endpoints;

public sealed record RecordPaymentRequest(Guid? EnrolmentId, decimal? Amount, string? Method, DateOnly? PaidOn, string[]? Months);

public sealed record VoidPaymentRequest(string? Reason);

public sealed record MonthRowDto(Guid EnrolmentId, int IndexNumber, string FullName, string Status, decimal Due, decimal Paid);

public sealed record MonthTotalsDto(int Paid, int PartlyPaid, int Unpaid, int FreeCard, decimal Due, decimal Collected, decimal Outstanding);

public sealed record ClassMonthDto(Guid ClassId, string Month, MonthTotalsDto Totals, MonthRowDto[] Rows);

public sealed record DueDto(string Month, decimal Due, decimal Paid, string Status);

public sealed record AllocationDto(string Month, decimal Amount);

public sealed record PaymentDto(
    Guid Id, Guid EnrolmentId, string ReceiptNo, decimal Amount, string Method, DateOnly PaidOn,
    DateTimeOffset? VoidedAt, string? VoidReason, AllocationDto[] Allocations);

public sealed record EnrolmentFeesDto(
    Guid EnrolmentId, Guid StudentId, string FullName, int IndexNumber, Guid ClassId, string ClassTitle,
    string FeePlan, decimal DiscountAmount, bool IsActive, decimal Outstanding, DueDto[] Dues, PaymentDto[] Payments);

/// <summary>Fees for the teacher: a class's month at a glance, one student's months and payments, recording and voiding payments.</summary>
internal static class FeeEndpoints
{
    private const int MonthsBack = 36;
    private const int MonthsAhead = 12;
    private const decimal MaxPayment = 10_000_000m;
    private const int MaxReasonLength = 200;

    public static void Map(RouteGroupBuilder group)
    {
        var fees = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        fees.MapGet("/classes/{classId:guid}", ClassMonth);
        fees.MapGet("/enrolments/{enrolmentId:guid}", EnrolmentFees);
        fees.MapPost("/payments", RecordPayment);
        fees.MapPost("/payments/{id:guid}/void", VoidPayment);
    }

    // One class for one month (this month by default): every billed student's status, with the class totals.
    private static async Task<IResult> ClassMonth(
        Guid classId, string? month, FeesDbContext db, FeeDueService dues, IClassDirectory classes, IClassRoster roster,
        TimeProvider clock, CancellationToken ct)
    {
        var current = FeeMonths.Current(clock);
        var chosen = month ?? current;
        if (!FeeMonths.IsValid(chosen) || string.CompareOrdinal(chosen, FeeMonths.Add(current, -MonthsBack)) < 0)
        {
            return Problems.Reject("month_invalid");
        }

        if (string.CompareOrdinal(chosen, current) > 0)
        {
            return Problems.Reject("month_in_future");
        }

        if (await classes.FindAsync(classId, ct) is null)
        {
            return Problems.Reject("class_not_found", StatusCodes.Status404NotFound);
        }

        await dues.EnsureForClassAsync(classId, [chosen], ct);
        var rows = await db.Dues.Where(d => d.ClassId == classId && d.Month == chosen).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. rows.Select(r => r.Id)], ct);

        // Active students, plus anyone who has since left but was billed this month.
        var people = (await roster.ActiveRosterAsync(classId, ct)).ToDictionary(r => r.EnrolmentId);
        var gone = rows.Select(r => r.EnrolmentId).Where(e => !people.ContainsKey(e)).ToList();
        if (gone.Count > 0)
        {
            foreach (var (id, entry) in await roster.FindEnrolmentsAsync(gone, ct))
            {
                people[id] = entry;
            }
        }

        var lines = rows
            .Where(r => people.ContainsKey(r.EnrolmentId))
            .Select(r =>
            {
                var p = people[r.EnrolmentId];
                var got = paid.GetValueOrDefault(r.Id);
                return new MonthRowDto(r.EnrolmentId, p.IndexNumber, p.FullName, FeeMonths.StatusOf(r.AmountDue, got).ToString(), r.AmountDue, got);
            })
            .OrderBy(r => r.IndexNumber)
            .ToArray();
        return Results.Ok(new ClassMonthDto(classId, chosen, Totals(lines), lines));
    }

    private static MonthTotalsDto Totals(MonthRowDto[] rows)
    {
        var billed = rows.Where(r => r.Status != nameof(MonthStatus.FreeCard)).ToList();
        var due = billed.Sum(r => r.Due);
        var collected = billed.Sum(r => r.Paid);
        return new MonthTotalsDto(
            rows.Count(r => r.Status == nameof(MonthStatus.Paid)), rows.Count(r => r.Status == nameof(MonthStatus.PartlyPaid)),
            rows.Count(r => r.Status == nameof(MonthStatus.Unpaid)), rows.Count(r => r.Status == nameof(MonthStatus.FreeCard)),
            due, collected, due - collected);
    }

    // One student's months, newest first, and every payment with the months it covered.
    private static async Task<IResult> EnrolmentFees(
        Guid enrolmentId, FeesDbContext db, FeeDueService dues, IClassDirectory classes, IClassRoster roster,
        TimeProvider clock, CancellationToken ct)
    {
        var basis = await roster.FindFeeBasisAsync(enrolmentId, ct);
        if (basis is null)
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        var current = FeeMonths.Current(clock);
        var joined = FeeMonths.Of(basis.JoinedOn);
        var first = string.CompareOrdinal(joined, FeeMonths.Add(current, -MonthsBack)) < 0 ? FeeMonths.Add(current, -MonthsBack) : joined;
        await dues.EnsureForEnrolmentAsync(basis, [.. FeeMonths.Range(first, current)], ct);

        var rows = await db.Dues.Where(d => d.EnrolmentId == enrolmentId).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. rows.Select(r => r.Id)], ct);
        var list = rows
            .Where(r => string.CompareOrdinal(r.Month, current) <= 0)
            .OrderByDescending(r => r.Month)
            .Select(r => new DueDto(r.Month, r.AmountDue, paid.GetValueOrDefault(r.Id), FeeMonths.StatusOf(r.AmountDue, paid.GetValueOrDefault(r.Id)).ToString()))
            .ToArray();

        var payments = await db.Payments.Where(p => p.EnrolmentId == enrolmentId).ToListAsync(ct);
        var cls = await classes.FindAsync(basis.ClassId, ct);
        return Results.Ok(new EnrolmentFeesDto(
            basis.EnrolmentId, basis.StudentId, basis.FullName, basis.IndexNumber, basis.ClassId, cls?.Title ?? "",
            basis.FeePlan, basis.DiscountAmount, basis.IsActive, list.Where(d => d.Status != nameof(MonthStatus.FreeCard)).Sum(d => d.Due - d.Paid), list,
            await ToDtosAsync(db, [.. payments.OrderByDescending(p => p.ReceiptNo)], ct)));
    }

    // The amount goes to the months asked for, oldest first, or to the oldest unpaid months when none are named.
    private static async Task<IResult> RecordPayment(
        RecordPaymentRequest request, FeesDbContext db, FeeDueService dues, IClassRoster roster, ITenantContext user,
        TimeProvider clock, CancellationToken ct)
    {
        var today = Problems.Today(clock);
        var current = FeeMonths.Current(clock);

        var basis = request.EnrolmentId is { } enrolmentId ? await roster.FindFeeBasisAsync(enrolmentId, ct) : null;
        if (basis is null)
        {
            return Problems.Reject("enrolment_not_found", StatusCodes.Status404NotFound);
        }

        if (request.Amount is not { } amount || amount <= 0 || amount > MaxPayment || decimal.Round(amount, 2) != amount)
        {
            return Problems.Reject("amount_invalid");
        }

        if (!Enum.TryParse<PaymentMethod>(request.Method ?? nameof(PaymentMethod.Cash), ignoreCase: true, out var method))
        {
            return Problems.Reject("method_invalid");
        }

        var paidOn = request.PaidOn ?? today;
        if (paidOn > today)
        {
            return Problems.Reject("paid_on_in_future");
        }

        var joined = FeeMonths.Of(basis.JoinedOn);
        var named = (request.Months ?? []).Distinct().Order().ToList();
        if (named.Any(m => !FeeMonths.IsValid(m) || string.CompareOrdinal(m, joined) < 0 || string.CompareOrdinal(m, FeeMonths.Add(current, MonthsAhead)) > 0))
        {
            return Problems.Reject("month_invalid");
        }

        // Months being paid ahead get their due now, so the money has somewhere to go.
        var toEnsure = named.Count > 0 ? named : [.. FeeMonths.Range(string.CompareOrdinal(joined, FeeMonths.Add(current, -MonthsBack)) < 0 ? FeeMonths.Add(current, -MonthsBack) : joined, current)];
        await dues.EnsureForEnrolmentAsync(basis, toEnsure, ct);

        var rows = await db.Dues.Where(d => d.EnrolmentId == basis.EnrolmentId && (named.Count == 0 || named.Contains(d.Month)))
            .ToListAsync(ct);
        rows = [.. rows.OrderBy(r => r.Month)];
        var paid = await FeeLedger.PaidByDueAsync(db, [.. rows.Select(r => r.Id)], ct);

        var left = amount;
        var shares = new List<(FeeDue Due, decimal Amount)>();
        foreach (var due in rows)
        {
            var owed = due.AmountDue - paid.GetValueOrDefault(due.Id);
            if (owed <= 0 || left <= 0)
            {
                continue;
            }

            var share = Math.Min(owed, left);
            shares.Add((due, share));
            left -= share;
        }

        if (shares.Count == 0)
        {
            return Problems.Reject("nothing_due");
        }

        if (left > 0)
        {
            return Problems.Reject("amount_exceeds_outstanding");
        }

        var dueIds = shares.Select(s => s.Due.Id).ToList();

        // Receipt numbers count up per teacher. If two payments race for the same number the unique index stops one, which tries again.
        for (var attempt = 0; ; attempt++)
        {
            var last = await db.Payments.OrderByDescending(p => p.ReceiptNo).Select(p => p.ReceiptNo).FirstOrDefaultAsync(ct);
            var next = last is not null && int.TryParse(last.AsSpan(2), out var n) ? n + 1 : 1;
            var payment = new Payment
            {
                TenantId = basis.TenantId, EnrolmentId = basis.EnrolmentId, ReceiptNo = $"R-{next:D6}", Amount = amount,
                Method = method, PaidOn = paidOn, ReceivedBy = user.UserId,
            };
            db.Payments.Add(payment);
            foreach (var (due, share) in shares)
            {
                db.Allocations.Add(new PaymentAllocation { TenantId = basis.TenantId, PaymentId = payment.Id, FeeDueId = due.Id, Amount = share });
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/fees/enrolments/{basis.EnrolmentId}", (await ToDtosAsync(db, [payment], ct))[0]);
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // The failed save left the new rows tracked; start clean and look up the same dues again.
                db.ChangeTracker.Clear();
                var again = await db.Dues.Where(d => dueIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
                shares = [.. shares.Select(s => (again[s.Due.Id], s.Amount))];
            }
        }
    }

    // A payment is never deleted. Voiding keeps it on record, with why, and gives its months back as owing.
    private static async Task<IResult> VoidPayment(
        Guid id, VoidPaymentRequest request, FeesDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var payment = await db.Payments.FindAsync([id], ct);
        if (payment is null)
        {
            return Results.NotFound();
        }

        var reason = Problems.Clean(request.Reason);
        if (reason is null)
        {
            return Problems.Reject("reason_required");
        }

        if (reason.Length > MaxReasonLength)
        {
            return Problems.Reject("reason_too_long");
        }

        if (payment.VoidedAt is not null)
        {
            return Problems.Reject("payment_voided", StatusCodes.Status409Conflict);
        }

        payment.VoidedAt = clock.GetUtcNow();
        payment.VoidReason = reason;
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ToDtosAsync(db, [payment], ct))[0]);
    }

    private static async Task<PaymentDto[]> ToDtosAsync(FeesDbContext db, List<Payment> payments, CancellationToken ct)
    {
        var ids = payments.Select(p => p.Id).ToList();
        var allocations = await db.Allocations.IgnoreQueryFilters().Where(a => ids.Contains(a.PaymentId)).ToListAsync(ct);
        var dueIds = allocations.Select(a => a.FeeDueId).Distinct().ToList();
        var months = await db.Dues.IgnoreQueryFilters().Where(d => dueIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Month, ct);

        return [.. payments.Select(p => new PaymentDto(
            p.Id, p.EnrolmentId, p.ReceiptNo, p.Amount, p.Method.ToString(), p.PaidOn, p.VoidedAt, p.VoidReason,
            [.. allocations.Where(a => a.PaymentId == p.Id).Select(a => new AllocationDto(months[a.FeeDueId], a.Amount)).OrderBy(a => a.Month)]))];
    }
}
