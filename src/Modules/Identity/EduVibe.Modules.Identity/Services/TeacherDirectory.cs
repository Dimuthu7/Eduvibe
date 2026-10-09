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
}
