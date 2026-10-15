using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Identity.Services;

internal sealed class TeacherDirectory(IdentityDbContext db) : ITeacherDirectory
{
    public async Task<IReadOnlySet<Guid>> FindActiveAsync(IEnumerable<Guid> teacherIds, CancellationToken cancellationToken = default)
    {
        var ids = teacherIds.Distinct().ToList();
        var found = await db.Teachers
            .Where(t => ids.Contains(t.Id) && db.Users.Any(u => u.Id == t.UserId && u.IsActive))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        return found.ToHashSet();
    }

    public async Task<string?> FindNameAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .Where(u => db.Teachers.Any(t => t.Id == teacherId && t.UserId == u.Id) && u.IsActive)
            .Select(u => new { u.FirstName, u.LastName })
            .SingleOrDefaultAsync(cancellationToken);
        return user is null ? null : $"{user.FirstName} {user.LastName}".Trim();
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        db.Teachers.CountAsync(t => db.Users.Any(u => u.Id == t.UserId && u.IsActive), cancellationToken);

    public async Task<TeacherIdentity?> FindIdentityAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        var row = await db.Teachers
            .Where(t => t.Id == teacherId)
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new { u.FirstName, u.LastName, u.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new TeacherIdentity(teacherId, $"{row.FirstName} {row.LastName}".Trim(), row.IsActive);
    }
}
