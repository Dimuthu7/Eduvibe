namespace GuruBook.Shared.Modules;

/// <summary>
/// The fee that covers one month, as a module outside Fees sees it when someone is about to pay it. The teacher filter is lifted to build it,
/// so the caller must already have established that the person asking is the student, their parent, or the teacher.
/// </summary>
/// <param name="Outstanding">What is still owed: the amount due less whatever has been paid toward it.</param>
public sealed record FeeDueView(
    Guid DueId, Guid TenantId, Guid EnrolmentId, Guid ClassId, string Month, string LastMonth, int PeriodMonths,
    DateOnly DueDate, decimal AmountDue, decimal Paid)
{
    public decimal Outstanding => AmountDue - Paid;
}

/// <summary>A payment that was recorded, or why it could not be.</summary>
public sealed record FeePaymentResult(Guid PaymentId, string ReceiptNo, string? Error = null)
{
    public static FeePaymentResult Failed(string error) => new(Guid.Empty, "", error);

    public bool Ok => Error is null;
}

/// <summary>
/// Lets another module read what a student owes and take money for it. The Fees module implements it; the Payments module uses it so that
/// a bank transfer a teacher confirms becomes an ordinary payment, with the same receipt numbers and the same alerts as cash taken by hand.
/// </summary>
public interface IFeeCollection
{
    /// <summary>The fee covering <paramref name="month"/> for one enrolment, or null when there is none.</summary>
    Task<FeeDueView?> FindDueAsync(Guid enrolmentId, string month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a payment for the signed-in teacher and tells the student and their parents, exactly as recording it by hand does.
    /// <paramref name="month"/> is the month the money goes to.
    /// </summary>
    Task<FeePaymentResult> RecordAsync(
        Guid enrolmentId, decimal amount, string method, DateOnly paidOn, string month, CancellationToken cancellationToken = default);
}
