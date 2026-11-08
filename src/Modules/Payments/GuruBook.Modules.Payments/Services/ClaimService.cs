using GuruBook.Modules.Payments.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Payments.Services;

/// <summary>What a payer is told on the pay screen, and the claim they have already made for this fee, if any.</summary>
public sealed record PayWayDto(
    bool CanPay, string Reference, decimal Outstanding, string Month, DateOnly DueDate, string ClassTitle,
    string BankName, string AccountName, string AccountNumber, string Branch, string Note,
    bool HasQr, long QrVersion, ClaimDto? Claim);

/// <summary>A claim as the person who made it sees it.</summary>
public sealed record ClaimDto(
    Guid Id, string Status, decimal Amount, string Month, string Reference, string Note, bool HasSlip,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? RejectReason, string? ReceiptNo);

/// <summary>
/// The claim side of paying by transfer, shared by the payer's screens and the teacher's. Nothing here trusts the caller: a payer only ever
/// reaches a fee through an enrolment the roster says is theirs, and a teacher only ever reaches claims their own tenant filter returns.
/// </summary>
internal sealed class ClaimService(
    PaymentsDbContext db, IFeeCollection fees, IClassRoster roster, IClassDirectory classes, IPlanGate plans,
    INotifier notifier, IMessageRouter messages, ITeacherDirectory teachers, ITenantContext user, TimeProvider clock)
{
    private const decimal MaxAmount = 10_000_000m;

    /// <summary>The enrolment, when it belongs to the signed-in student or to a child the parent is linked to. Null otherwise.</summary>
    public async Task<MyEnrolment?> FindPayerEnrolmentAsync(Guid? childStudentId, Guid enrolmentId, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return null;
        }

        var mine = childStudentId is { } studentId
            ? await roster.ActiveEnrolmentsOfChildAsync(userId, studentId, ct)
            : await roster.ActiveEnrolmentsOfUserAsync(userId, ct);
        return mine.FirstOrDefault(e => e.EnrolmentId == enrolmentId);
    }

    /// <summary>
    /// Of the payer's own enrolments, the ones whose teacher takes bank transfers and has filled their details in. The screens ask once and
    /// show a Pay button only for those, so a student whose teacher takes cash in person is never offered a dead end.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> PayableAsync(Guid? childStudentId, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return [];
        }

        var mine = childStudentId is { } studentId
            ? await roster.ActiveEnrolmentsOfChildAsync(userId, studentId, ct)
            : await roster.ActiveEnrolmentsOfUserAsync(userId, ct);

        var payable = new List<Guid>();
        foreach (var group in mine.GroupBy(e => e.TenantId))
        {
            if (!(await plans.ForTeacherAsync(group.Key, ct)).Has(PlanFeatures.BankPayments))
            {
                continue;
            }

            if (await db.BankDetails.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == group.Key, ct) is { IsFilled: true })
            {
                payable.AddRange(group.Select(e => e.EnrolmentId));
            }
        }

        return payable;
    }

    /// <summary>What to pay and where to send it. CanPay is false when the teacher is not on a plan with bank payments or has not filled their details in.</summary>
    public async Task<PayWayDto?> HowAsync(MyEnrolment enrolment, string month, CancellationToken ct)
    {
        if (await fees.FindDueAsync(enrolment.EnrolmentId, month, ct) is not { } due)
        {
            return null;
        }

        // The payer is not a teacher, so their teacher's row is read with the filter lifted and the tenant pinned to the class's owner.
        var details = await db.BankDetails.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == due.TenantId, ct);
        var qr = await db.Qr.IgnoreQueryFilters().AsNoTracking()
            .Where(q => q.TenantId == due.TenantId).Select(q => (DateTimeOffset?)q.UpdatedAt).FirstOrDefaultAsync(ct);
        var allowed = (await plans.ForTeacherAsync(due.TenantId, ct)).Has(PlanFeatures.BankPayments);
        var claim = await db.Claims.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.FeeDueId == due.DueId).OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);

        return new PayWayDto(
            allowed && details is { IsFilled: true }, PayReference.For(enrolment.EnrolmentId, due.Month), due.Outstanding, due.Month,
            // The payer is not a teacher, so the class is read the way their own screens read it.
            due.DueDate, (await classes.FindInAnyTenantAsync(enrolment.ClassId, ct))?.Title ?? "",
            details?.BankName ?? "", details?.AccountName ?? "", details?.AccountNumber ?? "", details?.Branch ?? "", details?.Note ?? "",
            qr is not null, qr?.ToUnixTimeMilliseconds() ?? 0, claim is null ? null : ToDto(claim));
    }

    /// <summary>Records "I've paid". One fee can have only one claim waiting, so a second one is refused rather than queued.</summary>
    public async Task<(PaymentClaim? Claim, string? Error, int Status)> CreateAsync(
        MyEnrolment enrolment, string month, decimal amount, string? note, bool byParent, CancellationToken ct)
    {
        if (await HowAsync(enrolment, month, ct) is not { } way)
        {
            return (null, "fee_not_found", StatusCodes.Status404NotFound);
        }

        if (!way.CanPay)
        {
            return (null, "bank_payments_off", StatusCodes.Status403Forbidden);
        }

        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            return (null, "amount_invalid", StatusCodes.Status400BadRequest);
        }

        if (way.Outstanding <= 0)
        {
            return (null, "nothing_due", StatusCodes.Status400BadRequest);
        }

        if (amount > way.Outstanding)
        {
            return (null, "amount_exceeds_outstanding", StatusCodes.Status400BadRequest);
        }

        if (way.Claim is { Status: nameof(ClaimStatus.Open) })
        {
            return (null, "claim_open", StatusCodes.Status409Conflict);
        }

        var due = await fees.FindDueAsync(enrolment.EnrolmentId, month, ct);
        var claim = new PaymentClaim
        {
            TenantId = enrolment.TenantId, EnrolmentId = enrolment.EnrolmentId, StudentId = enrolment.StudentId,
            ClassId = enrolment.ClassId, FeeDueId = due!.DueId, Month = due.Month, Amount = amount,
            Reference = way.Reference, Note = note ?? "", ClaimedBy = user.UserId ?? Guid.Empty, ByParent = byParent,
        };
        db.Claims.Add(claim);
        await db.SaveChangesAsync(ct);

        // The teacher is told once per claim, and several claims stay one line while they are unread.
        await notifier.NotifyAsync(
            [new NotifyRequest(
                enrolment.TenantId, "payment_claimed", new { amount, classTitle = way.ClassTitle, reference = way.Reference },
                Link: "/payments", GroupKey: "payment_claims", GroupedLink: "/payments")],
            ct);
        return (claim, null, StatusCodes.Status201Created);
    }

    /// <summary>The teacher's word that the money arrived: it becomes an ordinary bank transfer payment, with a receipt and the usual alerts.</summary>
    public async Task<(PaymentClaim? Claim, string? Error, int Status)> ConfirmAsync(Guid id, decimal? amount, CancellationToken ct)
    {
        if (await db.Claims.FindAsync([id], ct) is not { } claim)
        {
            return (null, "claim_not_found", StatusCodes.Status404NotFound);
        }

        if (claim.Status != ClaimStatus.Open)
        {
            return (null, "claim_decided", StatusCodes.Status409Conflict);
        }

        var taken = amount ?? claim.Amount;
        if (taken <= 0 || taken > MaxAmount || decimal.Round(taken, 2) != taken)
        {
            return (null, "amount_invalid", StatusCodes.Status400BadRequest);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);
        var result = await fees.RecordAsync(claim.EnrolmentId, taken, "BankTransfer", today, claim.Month, ct);
        if (!result.Ok)
        {
            return (null, result.Error, StatusCodes.Status400BadRequest);
        }

        claim.Status = ClaimStatus.Confirmed;
        claim.Amount = taken;
        claim.DecidedAt = clock.GetUtcNow();
        claim.PaymentId = result.PaymentId;
        claim.ReceiptNo = result.ReceiptNo;
        await db.SaveChangesAsync(ct);
        return (claim, null, StatusCodes.Status200OK);
    }

    /// <summary>Turned down, with a reason the payer reads. Nothing is recorded against the fee.</summary>
    public async Task<(PaymentClaim? Claim, string? Error, int Status)> RejectAsync(Guid id, string? reason, CancellationToken ct)
    {
        if (await db.Claims.FindAsync([id], ct) is not { } claim)
        {
            return (null, "claim_not_found", StatusCodes.Status404NotFound);
        }

        if (claim.Status != ClaimStatus.Open)
        {
            return (null, "claim_decided", StatusCodes.Status409Conflict);
        }

        if (reason is null)
        {
            return (null, "reason_required", StatusCodes.Status400BadRequest);
        }

        claim.Status = ClaimStatus.Rejected;
        claim.RejectReason = reason;
        claim.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        // Whoever said they had paid is told, so they can fix the reference or send the right slip.
        var title = (await classes.FindAsync(claim.ClassId, ct))?.Title ?? "";
        var rule = await messages.RuleAsync(MessageEvents.ClaimRejected, ct);
        if (claim.ClaimedBy != Guid.Empty && rule.On)
        {
            await notifier.NotifyAsync(
                [new NotifyRequest(
                    claim.ClaimedBy, "payment_claim_rejected", new { amount = claim.Amount, classTitle = title, reason },
                    Link: claim.ByParent ? "/family" : "/welcome", OnceKey: $"claim:{claim.Id}")],
                ct);
        }

        if (rule.On && MessageWays.IsPaid(rule.Way))
        {
            var name = (await roster.FindFeeBasisAsync(claim.EnrolmentId, ct))?.FullName ?? "";
            var teacher = claim.TenantId != Guid.Empty ? await teachers.FindNameAsync(claim.TenantId, ct) ?? "" : "";
            await messages.SendAsync(
                MessageEvents.ClaimRejected,
                [new MessageDraft(
                    claim.StudentId, [name, title, MessageTexts.MonthLabel(claim.Month), teacher], $"{MessageEvents.ClaimRejected}:{claim.Id}")],
                ct);
        }

        return (claim, null, StatusCodes.Status200OK);
    }

    public static ClaimDto ToDto(PaymentClaim c) => new(
        c.Id, c.Status.ToString(), c.Amount, c.Month, c.Reference, c.Note, c.HasSlip,
        c.CreatedAt, c.DecidedAt, c.RejectReason, c.ReceiptNo);
}
