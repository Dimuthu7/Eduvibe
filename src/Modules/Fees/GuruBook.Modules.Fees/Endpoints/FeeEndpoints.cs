using GuruBook.Modules.Fees.Domain;
using GuruBook.Modules.Fees.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Endpoints;

public sealed record RecordPaymentRequest(Guid? EnrolmentId, decimal? Amount, string? Method, DateOnly? PaidOn, string[]? Months);

public sealed record VoidPaymentRequest(string? Reason);

/// <summary>
/// One student's fee in a class for a month. Month is the first month the fee covers and PeriodMonths how many months it covers
/// (1 for a monthly class). Timing is Overdue, DueToday, DueSoon or empty; DaysLate counts days past DueDate.
/// </summary>
public sealed record MonthRowDto(
    Guid EnrolmentId, int IndexNumber, string FullName, string Status, decimal Due, decimal Paid,
    DateOnly DueDate = default, string Timing = "", int DaysLate = 0, string Month = "", int PeriodMonths = 1);

public sealed record MonthTotalsDto(int Paid, int PartlyPaid, int Unpaid, int FreeCard, decimal Due, decimal Collected, decimal Outstanding, int Overdue = 0);

public sealed record ClassMonthDto(Guid ClassId, string Month, MonthTotalsDto Totals, MonthRowDto[] Rows);

public sealed record DueDto(
    string Month, decimal Due, decimal Paid, string Status, DateOnly DueDate = default, int PeriodMonths = 1, string Timing = "", int DaysLate = 0, string LastMonth = "");

public sealed record AllocationDto(string Month, decimal Amount);

public sealed record PaymentDto(
    Guid Id, Guid EnrolmentId, string ReceiptNo, decimal Amount, string Method, DateOnly PaidOn,
    DateTimeOffset? VoidedAt, string? VoidReason, AllocationDto[] Allocations);

public sealed record EnrolmentFeesDto(
    Guid EnrolmentId, Guid StudentId, string FullName, int IndexNumber, Guid ClassId, string ClassTitle,
    string FeePlan, decimal DiscountAmount, bool IsActive, decimal Outstanding, DueDto[] Dues, PaymentDto[] Payments,
    ReminderDto[]? Reminders = null, bool RemindersSkipped = false);

/// <summary>Fees for the teacher: a class's month at a glance, one student's months and payments, recording and voiding payments.</summary>
internal static class FeeEndpoints
{
    private const int MonthsBack = ClassMonthService.MonthsBack;
    private const int MonthsAhead = 12;
    private const decimal MaxPayment = 10_000_000m;
    private const int MaxReasonLength = 200;
    private const int RemindersShown = 12;

    public static void Map(RouteGroupBuilder group)
    {
        var fees = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        fees.MapGet("/classes/{classId:guid}", ClassMonth);
        fees.MapGet("/enrolments/{enrolmentId:guid}", EnrolmentFees);
        fees.MapPost("/payments", RecordPayment);
        fees.MapPost("/payments/{id:guid}/void", VoidPayment);
    }

    // One class for one month (this month by default): every billed student's status, with the class totals.
    private static async Task<IResult> ClassMonth(Guid classId, string? month, ClassMonthService months, CancellationToken ct)
    {
        var result = await months.BuildAsync(classId, month, ct);
        return result.Value is { } value
            ? Results.Ok(value)
            : Problems.Reject(result.Error!, result.Error == "class_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
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
        var today = FeeMonths.Today(clock);
        var joined = FeeMonths.Of(basis.JoinedOn);
        var first = string.CompareOrdinal(joined, FeeMonths.Add(current, -MonthsBack)) < 0 ? FeeMonths.Add(current, -MonthsBack) : joined;
        await dues.EnsureForEnrolmentAsync(basis, [.. FeeMonths.Range(first, current)], ct);

        var rows = await db.Dues.Where(d => d.EnrolmentId == enrolmentId).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. rows.Select(r => r.Id)], ct);
        var list = rows
            .Where(r => string.CompareOrdinal(r.Month, current) <= 0)
            .OrderByDescending(r => r.Month)
            .Select(r => FeeTiming.ToDto(r, paid.GetValueOrDefault(r.Id), today))
            .ToArray();

        var payments = await db.Payments.Where(p => p.EnrolmentId == enrolmentId).ToListAsync(ct);
        var cls = await classes.FindAsync(basis.ClassId, ct);

        // The latest reminders that went out for this student, newest first (by id: ids are time ordered, and the database cannot sort offsets everywhere).
        var sent = await db.Reminders.Where(r => r.EnrolmentId == enrolmentId).OrderByDescending(r => r.Id).Take(RemindersShown).ToListAsync(ct);
        var months = rows.ToDictionary(r => r.Id, r => r.Month);
        var reminders = sent.OrderByDescending(r => r.CreatedAt).Select(r => new ReminderDto(r.Step, r.Number, months.GetValueOrDefault(r.DueId, ""), r.CreatedAt, r.Channel)).ToArray();
        var skipped = await db.ReminderSkips.AnyAsync(s => s.EnrolmentId == enrolmentId, ct);
        return Results.Ok(new EnrolmentFeesDto(
            basis.EnrolmentId, basis.StudentId, basis.FullName, basis.IndexNumber, basis.ClassId, cls?.Title ?? "",
            basis.FeePlan, basis.DiscountAmount, basis.IsActive, list.Where(d => d.Status != nameof(MonthStatus.FreeCard)).Sum(d => d.Due - d.Paid), list,
            await ToDtosAsync(db, [.. payments.OrderByDescending(p => p.ReceiptNo)], ct), reminders, skipped));
    }

    // The amount goes to the months asked for, oldest first, or to the oldest unpaid months when none are named.
    private static async Task<IResult> RecordPayment(
        RecordPaymentRequest request, FeesDbContext db, FeeDueService dues, IClassRoster roster, IClassDirectory classes, INotifier notifier,
        IParentLinks parents, ITenantContext user, TimeProvider clock, CancellationToken ct)
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

        var rows = (await db.Dues.Where(d => d.EnrolmentId == basis.EnrolmentId).ToListAsync(ct))
            .Where(d => named.Count == 0 || named.Any(d.Covers))
            .OrderBy(r => r.Month)
            .ToList();
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
                var title = (await classes.FindAsync(basis.ClassId, ct))?.Title ?? "";
                var told = new List<NotifyRequest>();
                if (basis.UserId is { } studentUser)
                {
                    told.Add(new NotifyRequest(
                        studentUser, "payment_received", new { amount, receiptNo = payment.ReceiptNo, classTitle = title },
                        Link: "/welcome", OnceKey: $"payment:{payment.Id}"));
                }

                // The parents a teacher linked hear it too, with the child named, so a parent of two knows which one it is about.
                foreach (var parent in await ParentsOfAsync(parents, basis.StudentId, ct))
                {
                    told.Add(new NotifyRequest(
                        parent, "child_payment",
                        new { student = basis.FullName, amount, receiptNo = payment.ReceiptNo, classTitle = title },
                        Link: "/family", OnceKey: $"payment:{payment.Id}", Topic: AlertTopics.Payments, AboutStudentId: basis.StudentId));
                }

                await notifier.NotifyAsync(told, ct);

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
        Guid id, VoidPaymentRequest request, FeesDbContext db, IClassRoster roster, IClassDirectory classes, IParentLinks parents,
        INotifier notifier, TimeProvider clock, CancellationToken ct)
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
        await TellAboutVoidAsync(payment, roster, classes, parents, notifier, ct);
        return Results.Ok((await ToDtosAsync(db, [payment], ct))[0]);
    }

    /// <summary>A payment taken back is a correction: whoever was told it came in is told it was undone, once per payment.</summary>
    private static async Task TellAboutVoidAsync(
        Payment payment, IClassRoster roster, IClassDirectory classes, IParentLinks parents, INotifier notifier, CancellationToken ct)
    {
        if (await roster.FindFeeBasisAsync(payment.EnrolmentId, ct) is not { } basis)
        {
            return;
        }

        var title = (await classes.FindAsync(basis.ClassId, ct))?.Title ?? "";
        var told = new List<NotifyRequest>();
        if (basis.UserId is { } studentUser)
        {
            told.Add(new NotifyRequest(
                studentUser, "payment_voided", new { amount = payment.Amount, receiptNo = payment.ReceiptNo, classTitle = title },
                Link: "/welcome", OnceKey: $"void:{payment.Id}"));
        }

        foreach (var parent in await ParentsOfAsync(parents, basis.StudentId, ct))
        {
            told.Add(new NotifyRequest(
                parent, "child_payment_voided",
                new { student = basis.FullName, amount = payment.Amount, receiptNo = payment.ReceiptNo, classTitle = title },
                Link: "/family", OnceKey: $"void:{payment.Id}", Topic: AlertTopics.Payments, AboutStudentId: basis.StudentId));
        }

        await notifier.NotifyAsync(told, ct);
    }

    private static async Task<IReadOnlyList<Guid>> ParentsOfAsync(IParentLinks parents, Guid studentId, CancellationToken ct) =>
        (await parents.ParentsOfAsync([studentId], ct)).TryGetValue(studentId, out var linked) ? linked : [];

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
