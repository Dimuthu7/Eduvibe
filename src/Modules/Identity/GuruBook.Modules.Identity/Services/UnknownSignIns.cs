using Microsoft.Extensions.Caching.Memory;

namespace GuruBook.Modules.Identity.Services;

/// <summary>
/// Counts wrong sign-ins for names that have no account, the same way a real account counts them, so the "too many tries" pause comes
/// after five tries whether the name exists or not and cannot be used to find out which usernames or phone numbers have accounts.
/// Kept in memory only: after a restart the count starts again, which gives nothing away.
/// </summary>
public sealed class UnknownSignIns : IDisposable
{
    // Sign-in is rate limited per address, so this many names is far more than one day of guessing can reach.
    private readonly MemoryCache _tries = new(new MemoryCacheOptions { SizeLimit = 20_000 });

    public bool IsLocked(string name, DateTimeOffset now) => _tries.TryGetValue(name, out Tries? tries) && tries!.LockedUntil > now;

    /// <summary>Counts one wrong try; the fifth one pauses the name, as for a real account.</summary>
    public void Failed(string name, DateTimeOffset now)
    {
        var tries = _tries.GetOrCreate(name, entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = TimeSpan.FromDays(1);
            return new Tries();
        })!;

        lock (tries)
        {
            tries.Failed++;
            if (tries.Failed >= AuthService.MaxFailedLogins)
            {
                tries.LockedUntil = now + AuthService.LockDuration;
                tries.Failed = 0;
            }
        }
    }

    public void Dispose() => _tries.Dispose();

    private sealed class Tries
    {
        public int Failed;
        public DateTimeOffset? LockedUntil;
    }
}
