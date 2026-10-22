using GuruBook.Modules.Exams.Domain;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Exams.Services;

/// <summary>Finds the current teacher's grade scheme, making it from the O/L default the first time.</summary>
public sealed class GradeSchemeService(ExamsDbContext db)
{
    public const string DefaultName = "O/L";

    public async Task<GradeScheme> EnsureAsync(CancellationToken ct)
    {
        var scheme = await db.GradeSchemes.SingleOrDefaultAsync(ct);
        if (scheme is not null)
        {
            return scheme;
        }

        scheme = new GradeScheme { Name = DefaultName };
        db.GradeSchemes.Add(scheme);
        db.GradeBands.AddRange(ExamCalculator.OlDefault.Select(b => new GradeBand { GradeSchemeId = scheme.Id, Grade = b.Grade, MinPercent = b.MinPercent }));
        try
        {
            await db.SaveChangesAsync(ct);
            return scheme;
        }
        catch (DbUpdateException)
        {
            // Another request made it first: the unique index on the teacher keeps it to one.
            db.ChangeTracker.Clear();
            return await db.GradeSchemes.SingleAsync(ct);
        }
    }

    public async Task<List<BandRule>> BandsAsync(Guid schemeId, CancellationToken ct)
    {
        var bands = await db.GradeBands.AsNoTracking().Where(b => b.GradeSchemeId == schemeId).ToListAsync(ct);
        return [.. bands.OrderByDescending(b => b.MinPercent).Select(b => new BandRule(b.Grade, b.MinPercent))];
    }
}
