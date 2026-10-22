namespace GuruBook.Shared.Modules;

/// <summary>
/// One notification for one person. Kind tells the app which message to show; Params are the words that go into it (names, amounts, dates).
/// The text itself is never stored, so it is shown in the person's language.
/// Give <see cref="OnceKey"/> for something that must reach a person once however many times it is asked for (the same exam published twice),
/// or <see cref="GroupKey"/> for something that repeats and should stay one line while it is unread (three students asking to join one class).
/// </summary>
/// <param name="RecipientId">Who to tell: a student's sign-in account id, or a teacher's id (the tenant id). Teachers and accounts have different ids, so the two never clash.</param>
/// <param name="Link">Where the app goes when the notification is tapped, for example "/results". Null for none.</param>
/// <param name="GroupedLink">The link to use once the notification stands for more than one event.</param>
public sealed record NotifyRequest(
    Guid RecipientId, string Kind, object? Params = null, string? Link = null, string? OnceKey = null, string? GroupKey = null, string? GroupedLink = null);

/// <summary>
/// Lets a module tell a person something without knowing how notifications are stored. The Notifications module implements it.
/// A notification must never break the action that caused it, so a failure is logged and swallowed.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(IReadOnlyCollection<NotifyRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>Deletes everything addressed to someone who is being removed: a student account or a teacher.</summary>
    Task ForgetAsync(Guid recipientId, CancellationToken cancellationToken = default);
}
