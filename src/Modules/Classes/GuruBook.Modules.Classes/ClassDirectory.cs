using GuruBook.Modules.Classes.Domain;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Classes;

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

    public async Task<IReadOnlyDictionary<Guid, ClassSummary>> FindManyInAnyTenantAsync(IEnumerable<Guid> classIds, CancellationToken cancellationToken = default)
    {
        var ids = classIds.Distinct().ToList();
        return await db.Classes.IgnoreQueryFilters().Where(c => ids.Contains(c.Id)).Select(Summary).ToDictionaryAsync(c => c.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ClassMeeting>> MeetingsOnAsync(int dayOfWeek, CancellationToken cancellationToken = default)
    {
        var rows = await db.ClassSlots
            .Where(s => s.Day == dayOfWeek)
            .Join(db.Classes.Where(c => c.Status == ClassStatus.Active), s => s.ClassId, c => c.Id,
                (s, c) => new { c.Id, c.Title, s.Start, s.End })
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(r => r.Start).ThenBy(r => r.Title).Select(r => new ClassMeeting(r.Id, r.Title, r.Start, r.End))];
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ClassWeeklySlot>>> SlotsInAnyTenantAsync(
        IEnumerable<Guid> classIds, CancellationToken cancellationToken = default)
    {
        var ids = classIds.Distinct().ToList();
        var rows = await db.ClassSlots.IgnoreQueryFilters().Where(s => ids.Contains(s.ClassId)).ToListAsync(cancellationToken);
        return rows.GroupBy(s => s.ClassId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<ClassWeeklySlot>)[.. g.OrderBy(s => s.Day).ThenBy(s => s.Start).Select(s => new ClassWeeklySlot(s.Day, s.Start, s.End))]);
    }

    private static readonly System.Linq.Expressions.Expression<Func<TuitionClass, ClassSummary>> Summary =
        c => new ClassSummary(c.Id, c.TenantId, c.Title, c.MonthlyFee, c.Status == ClassStatus.Archived);
}
