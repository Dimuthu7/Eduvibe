namespace GuruBook.Shared.Modules;

/// <summary>
/// Turns the code on a printed student card back into the student. The Students module implements it; Attendance uses it to
/// mark a student present when their card is scanned. Only the current teacher's own cards are found.
/// </summary>
public interface IStudentCards
{
    /// <summary>The student a card of the current teacher belongs to, or null when the code is not one of their cards.</summary>
    Task<Guid?> StudentOfAsync(string code, CancellationToken cancellationToken = default);
}
