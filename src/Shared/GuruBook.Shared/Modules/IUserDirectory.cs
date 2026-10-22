namespace GuruBook.Shared.Modules;

/// <summary>Lets other modules show who a user is, such as who made a change in the audit log. Identity implements it.</summary>
public interface IUserDirectory
{
    /// <summary>Full names by user id, for the ids that belong to a user.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
}
