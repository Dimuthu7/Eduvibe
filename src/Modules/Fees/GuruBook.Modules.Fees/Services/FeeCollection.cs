using GuruBook.Modules.Fees.Domain;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Services;

/// <summary>
/// What the Payments module needs from fees: what a student still owes for a month, and a way to take the money once the teacher has
/// confirmed a bank transfer. Reading a due lifts the teacher filter, because a student paying their own fee is not a teacher; the caller
/// has already checked that the enrolment is theirs. Recording runs as the signed-in teacher, like any other payment.
/// </summary>
internal sealed class FeeCollection(FeesDbContext db, PaymentRecorder recorder, IClassRoster roster, TimeProvider clock) : IFeeCollection
{
    public async Task<FeeDueView?> FindDueAsync(Guid enrolmentId, string month, CancellationToken ct = default)
    {
        if (!FeeMonths.IsValid(month))
        {
            return null;
        }

        var rows = await db.Dues.IgnoreQueryFilters().AsNoTracking().Where(d => d.EnrolmentId == enrolmentId).ToListAsync(ct);
        if (rows.FirstOrDefault(d => d.Covers(month)) is not { } due)
        {
            return null;
        }

        var paid = await FeeLedger.PaidByDueAsync(db, [due.Id], ct);
        return new FeeDueView(
            due.Id, due.TenantId, due.EnrolmentId, due.ClassId, due.Month, due.LastMonth, due.PeriodMonths,
            due.DueDate, due.AmountDue, paid.GetValueOrDefault(due.Id));
    }

    public async Task<FeePaymentResult> RecordAsync(
        Guid enrolmentId, decimal amount, string method, DateOnly paidOn, string month, CancellationToken ct = default)
    {
        if (await roster.FindFeeBasisAsync(enrolmentId, ct) is not { } basis)
        {
            return FeePaymentResult.Failed("enrolment_not_found");
        }

        if (!Enum.TryParse<PaymentMethod>(method, ignoreCase: true, out var parsed))
        {
            return FeePaymentResult.Failed("method_invalid");
        }

        if (!FeeMonths.IsValid(month))
        {
            return FeePaymentResult.Failed("month_invalid");
        }

        if (paidOn > FeeMonths.Today(clock))
        {
            return FeePaymentResult.Failed("paid_on_in_future");
        }

        var (payment, error) = await recorder.RecordAsync(basis, amount, parsed, paidOn, [month], receivedBy: null, ct);
        return payment is null ? FeePaymentResult.Failed(error!) : new FeePaymentResult(payment.Id, payment.ReceiptNo);
    }
}
