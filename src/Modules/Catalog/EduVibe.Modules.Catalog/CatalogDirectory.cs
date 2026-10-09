using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Catalog;

internal sealed class CatalogDirectory(CatalogDbContext db) : ICatalogDirectory
{
    public Task<bool> StreamIsActiveAsync(Guid streamId, CancellationToken cancellationToken = default) =>
        db.Streams.AnyAsync(s => s.Id == streamId && s.IsActive, cancellationToken);

    public async Task<IReadOnlySet<Guid>> FindActiveSubjectsAsync(IEnumerable<Guid> subjectIds, CancellationToken cancellationToken = default)
    {
        var ids = subjectIds.Distinct().ToList();
        var found = await db.Subjects.Where(s => ids.Contains(s.Id) && s.IsActive).Select(s => s.Id).ToListAsync(cancellationToken);
        return found.ToHashSet();
    }
}
