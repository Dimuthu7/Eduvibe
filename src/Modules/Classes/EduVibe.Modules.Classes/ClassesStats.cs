using EduVibe.Modules.Classes.Domain;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Classes;

/// <summary>Institutes and classes for the Super Admin dashboard. Read by the Super Admin, who sees every teacher's rows.</summary>
internal sealed class ClassesStats(ClassesDbContext db) : IStatsSource
{
    public async Task<IReadOnlyList<Stat>> ReadAsync(CancellationToken cancellationToken = default) =>
    [
        new Stat("institutes", await db.Institutes.CountAsync(cancellationToken)),
        new Stat("classes", await db.Classes.CountAsync(c => c.Status == ClassStatus.Active, cancellationToken)),
    ];
}
