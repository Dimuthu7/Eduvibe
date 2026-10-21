namespace EduVibe.Shared.Modules;

/// <summary>
/// How to reach a student or their parent. <see cref="MessageNumber"/> is the guardian's number when there is one, otherwise the student's own:
/// a message about fees or absence is for the parent.
/// </summary>
public sealed record StudentContact(Guid StudentId, string FirstName, string FullName, string? Phone, string? GuardianPhone)
{
    public string? MessageNumber => string.IsNullOrWhiteSpace(GuardianPhone) ? Phone : GuardianPhone;
}

/// <summary>Contact details of the current teacher's students. The Students module implements it; the WhatsApp module uses it.</summary>
public interface IStudentContacts
{
    /// <summary>The contacts of those among <paramref name="studentIds"/> that the current teacher has (or had) in a class, by student id.</summary>
    Task<IReadOnlyDictionary<Guid, StudentContact>> FindAsync(IEnumerable<Guid> studentIds, CancellationToken cancellationToken = default);
}
