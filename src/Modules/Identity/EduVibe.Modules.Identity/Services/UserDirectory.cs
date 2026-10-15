using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Identity.Services;

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(cancellationToken);
        return users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
    }
}

/// <summary>Teachers for the Super Admin dashboard.</summary>
internal sealed class IdentityStats(IdentityDbContext db) : IStatsSource
{
    public async Task<IReadOnlyList<Stat>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var active = await db.Teachers.CountAsync(t => db.Users.Any(u => u.Id == t.UserId && u.IsActive), cancellationToken);
        var all = await db.Teachers.CountAsync(cancellationToken);
        return [new Stat("teachers", active), new Stat("teachers_inactive", all - active)];
    }
}
