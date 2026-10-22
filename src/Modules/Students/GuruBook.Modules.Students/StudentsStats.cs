using GuruBook.Modules.Students.Domain;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students;

/// <summary>Students for the Super Admin dashboard: people in at least one class right now. Read by the Super Admin, who sees every teacher's rows.</summary>
internal sealed class StudentsStats(StudentsDbContext db) : IStatsSource
{
    public async Task<IReadOnlyList<Stat>> ReadAsync(CancellationToken cancellationToken = default) =>
    [
        new Stat("students", await db.Enrolments.Where(e => e.Status == EnrolmentStatus.Active).Select(e => e.StudentId).Distinct().CountAsync(cancellationToken)),
    ];
}
