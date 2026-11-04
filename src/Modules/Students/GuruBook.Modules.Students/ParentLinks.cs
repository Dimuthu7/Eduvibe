using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students;

/// <summary>
/// The parents the signed-in teacher has linked to their students. The teacher filter does the rest: a parent linked by another teacher
/// is none of this teacher's business, and nothing is returned at all while the plan does not include the parent portal.
/// </summary>
internal sealed class ParentLinkDirectory(StudentsDbContext db, IPlanGate plans) : IParentLinks
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ParentsOfAsync(
        IEnumerable<Guid> studentIds, CancellationToken cancellationToken = default)
    {
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0 || !(await plans.CurrentAsync(cancellationToken)).Has(PlanFeatures.ParentPortal))
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        var rows = await db.ParentLinks.AsNoTracking()
            .Where(l => ids.Contains(l.StudentId))
            .Select(l => new { l.StudentId, l.ParentUserId })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(r => r.ParentUserId).Distinct()]);
    }
}
