namespace GuruBook.Shared.Modules;

public sealed record NewStudentAccount(string Phone, string FirstName, string LastName, string? PasswordHash);

public sealed record NewParentAccount(string Phone, string FirstName, string LastName);

public enum AccountOutcome
{
    /// <summary>A new account was made.</summary>
    Created,

    /// <summary>The phone number already had an account, which now also has the Student role.</summary>
    Linked,

    /// <summary>The phone number already has an account and linking was not allowed.</summary>
    PhoneInUse,
}

/// <param name="OneTimePassword">Set only when the account was created without a chosen password.</param>
public sealed record AccountResult(AccountOutcome Outcome, Guid? UserId, string? OneTimePassword);

/// <summary>
/// Lets the Students module create and reset student sign-in accounts without reading the Identity
/// module's tables. Identity implements it.
/// </summary>
public interface IAccountProvisioner
{
    int MinimumPasswordLength { get; }

    /// <summary>Hashes a password the person chose, so it can be kept until an account is made.</summary>
    string HashPassword(string password);

    /// <summary>
    /// Makes a student account. Without a password hash the account gets a one-time password that must be
    /// replaced at first sign-in. With <paramref name="linkExisting"/> a phone number that already has an
    /// account gets the Student role instead of failing.
    /// </summary>
    Task<AccountResult> CreateStudentAccountAsync(NewStudentAccount account, bool linkExisting, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a parent account for a guardian phone number with a one-time password, or, when the number already has an account (another
    /// teacher's link, or a teacher or student who is also a parent), adds the Parent role to it and keeps its password.
    /// </summary>
    Task<AccountResult> CreateParentAccountAsync(NewParentAccount account, CancellationToken cancellationToken = default);

    /// <summary>Gives a parent a new one-time password. Returns null for an account that has any role besides Parent.</summary>
    Task<string?> ResetParentPasswordAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Takes the Parent role off an account, and deletes the account when nothing else is left on it.</summary>
    Task RemoveParentAccountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Gives a student a new one-time password. Returns null for an account that is not a plain student account.</summary>
    Task<string?> ResetStudentPasswordAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the Student role off an account, and deletes the account when nothing else is left on it (a teacher who is also a student keeps
    /// their teacher account). What the audit log says about a deleted account is blanked. Used when a student asks for their data to be erased.
    /// </summary>
    Task RemoveStudentAccountAsync(Guid userId, CancellationToken cancellationToken = default);
}
