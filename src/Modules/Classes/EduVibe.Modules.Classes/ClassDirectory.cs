using EduVibe.Modules.Classes.Domain;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Classes;

internal sealed class ClassDirectory(ClassesDbContext db) : IClassDirectory
{
    public async Task<ClassSummary?> FindAsync(Guid classId, CancellationToken cancellationToken = default) =>
        await db.Classes.Where(c => c.Id == classId).Select(Summary).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ClassSummary>> FindManyAsync(IEnumerable<Guid> classIds, CancellationToken cancellationToken = default)
    {
        var ids = classIds.Distinct().ToList();
        return await db.Classes.Where(c => ids.Contains(c.Id)).Select(Summary).ToDictionaryAsync(c => c.Id, cancellationToken);
    }

    public async Task<ClassSummary?> FindInAnyTenantAsync(Guid classId, CancellationToken cancellationToken = default) =>
        await db.Classes.IgnoreQueryFilters().Where(c => c.Id == classId).Select(Summary).SingleOrDefaultAsync(cancellationToken);

    private static readonly System.Linq.Expressions.Expression<Func<TuitionClass, ClassSummary>> Summary =
        c => new ClassSummary(c.Id, c.TenantId, c.Title, c.MonthlyFee, c.Status == ClassStatus.Archived);
}
