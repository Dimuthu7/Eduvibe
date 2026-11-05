namespace GuruBook.Shared.Modules;

/// <summary>
/// The parent accounts a teacher has linked to their students. The Students module implements it; Fees, Attendance and Exams use it to
/// tell parents what happened to their child. Only teachers whose plan includes the parent portal have links returned, so a teacher who
/// drops the plan stops telling parents anything, just as the parent portal itself goes quiet.
/// </summary>
public interface IParentLinks
{
    /// <summary>The parent accounts linked to each of <paramref name="studentIds"/> by the current teacher. Students with no parent are left out.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ParentsOfAsync(IEnumerable<Guid> studentIds, CancellationToken cancellationToken = default);
}
