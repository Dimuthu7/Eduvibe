using GuruBook.Modules.Fees.Domain;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Services;

/// <summary>
/// Takes money for one enrolment: it spreads the amount over the months it is for, gives the payment the teacher's next receipt number, and
/// tells the student and the parents a teacher linked, in the app and, when the teacher asked for that, on WhatsApp or by text. Both recording a
/// payment by hand and confirming a bank transfer come through here, so a receipt looks the same whichever way the money arrived.
/// </summary>
internal sealed class PaymentRecorder(
    FeesDbContext db, FeeDueService dues, IClassDirectory classes, IParentLinks parents, INotifier notifier, IMessageRouter messages,
    ITeacherDirectory teachers, TimeProvider clock)
{
    private const int MonthsBack = ClassMonthService.MonthsBack;

    /// <summary>
    /// Puts <paramref name="amount"/> toward <paramref name="months"/>, oldest first, or toward the oldest unpaid months when none are named.
    /// Returns the payment, or the code of what was wrong with it.
    /// </summary>
    public async Task<(Payment? Payment, string? Error)> RecordAsync(
        FeeBasis basis, decimal amount, PaymentMethod method, DateOnly paidOn, IReadOnlyList<string> months, Guid? receivedBy, CancellationToken ct)
    {
        var current = FeeMonths.Current(clock);
        var joined = FeeMonths.Of(basis.JoinedOn);
        var named = months.Distinct().Order().ToList();

        // Months being paid ahead get their due now, so the money has somewhere to go.
        var first = string.CompareOrdinal(joined, FeeMonths.Add(current, -MonthsBack)) < 0 ? FeeMonths.Add(current, -MonthsBack) : joined;
        await dues.EnsureForEnrolmentAsync(basis, named.Count > 0 ? named : [.. FeeMonths.Range(first, current)], ct);

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
            return (null, "nothing_due");
        }

        if (left > 0)
        {
            return (null, "amount_exceeds_outstanding");
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
                Method = method, PaidOn = paidOn, ReceivedBy = receivedBy,
            };
            db.Payments.Add(payment);
            foreach (var (due, share) in shares)
            {
                db.Allocations.Add(new PaymentAllocation { TenantId = basis.TenantId, PaymentId = payment.Id, FeeDueId = due.Id, Amount = share });
            }

            try
            {
                await db.SaveChangesAsync(ct);
                await TellAboutPaymentAsync(basis, payment, ct);
                return (payment, null);
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

    /// <summary>A payment taken back is a correction: whoever was told it came in is told it was undone, once per payment.</summary>
    public async Task TellAboutVoidAsync(FeeBasis basis, Payment payment, CancellationToken ct)
    {
        var title = await TitleOfAsync(basis, ct);
        var told = new List<NotifyRequest>();
        if (basis.UserId is { } studentUser)
        {
            told.Add(new NotifyRequest(
                studentUser, "payment_voided", new { amount = payment.Amount, receiptNo = payment.ReceiptNo, classTitle = title },
                Link: "/welcome", OnceKey: $"void:{payment.Id}"));
        }

        foreach (var parent in await ParentsOfAsync(basis.StudentId, ct))
        {
            told.Add(new NotifyRequest(
                parent, "child_payment_voided",
                new { student = basis.FullName, amount = payment.Amount, receiptNo = payment.ReceiptNo, classTitle = title },
                Link: "/family", OnceKey: $"void:{payment.Id}", Topic: AlertTopics.Payments, AboutStudentId: basis.StudentId));
        }

        await notifier.NotifyAsync(told, ct);
    }

    private async Task TellAboutPaymentAsync(FeeBasis basis, Payment payment, CancellationToken ct)
    {
        var rule = await messages.RuleForAsync(basis.TenantId, MessageEvents.PaymentReceived, ct);
        if (!rule.On)
        {
            return;
        }

        var title = await TitleOfAsync(basis, ct);
        var told = new List<NotifyRequest>();
        if (basis.UserId is { } studentUser)
        {
            told.Add(new NotifyRequest(
                studentUser, "payment_received", new { amount = payment.Amount, receiptNo = payment.ReceiptNo, classTitle = title },
                Link: "/welcome", OnceKey: $"payment:{payment.Id}"));
        }

        // The parents a teacher linked hear it too, with the child named, so a parent of two knows which one it is about.
        foreach (var parent in await ParentsOfAsync(basis.StudentId, ct))
        {
            told.Add(new NotifyRequest(
                parent, "child_payment",
                new { student = basis.FullName, amount = payment.Amount, receiptNo = payment.ReceiptNo, classTitle = title },
                Link: "/family", OnceKey: $"payment:{payment.Id}", Topic: AlertTopics.Payments, AboutStudentId: basis.StudentId));
        }

        await notifier.NotifyAsync(told, ct);
        if (MessageWays.IsPaid(rule.Way))
        {
            var teacher = await teachers.FindNameAsync(basis.TenantId, ct) ?? "";
            await messages.SendForAsync(
                basis.TenantId, MessageEvents.PaymentReceived,
                [new MessageDraft(
                    basis.StudentId, [basis.FullName, title, MessageTexts.Lkr(payment.Amount), payment.ReceiptNo, teacher],
                    $"{MessageEvents.PaymentReceived}:{payment.Id}")],
                ct);
        }
    }

    private async Task<string> TitleOfAsync(FeeBasis basis, CancellationToken ct) =>
        (await classes.FindAsync(basis.ClassId, ct))?.Title ?? "";

    private async Task<IReadOnlyList<Guid>> ParentsOfAsync(Guid studentId, CancellationToken ct) =>
        (await parents.ParentsOfAsync([studentId], ct)).TryGetValue(studentId, out var linked) ? linked : [];
}
