using GuruBook.Modules.Exams.Domain;
using GuruBook.Modules.Exams.Services;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Exams.Endpoints;

public sealed record SaveGradeSchemeRequest(BandRequest[]? Bands);

public sealed record BandRequest(string? Grade, decimal? MinPercent);

/// <summary>The teacher's grade scheme: the O/L default at first, editable after that.</summary>
internal static class GradeSchemeEndpoints
{
    private const int MaxBands = 10;
    private const int MaxGradeLength = 3;

    public static void Map(RouteGroupBuilder group)
    {
        var scheme = group.MapGroup("/grade-scheme").RequireAuthorization(Policies.Teacher);
        scheme.MapGet("", Get);
        scheme.MapPut("", Save);
    }

    private static async Task<IResult> Get(GradeSchemeService schemes, CancellationToken ct)
    {
        var scheme = await schemes.EnsureAsync(ct);
        return Results.Ok(ToDto(scheme, await schemes.BandsAsync(scheme.Id, ct)));
    }

    // The bands are replaced as a whole. Grades already shown follow the new bands at once, because a grade is
    // worked out from the percentage whenever it is read.
    private static async Task<IResult> Save(SaveGradeSchemeRequest request, ExamsDbContext db, GradeSchemeService schemes, CancellationToken ct)
    {
        var bands = request.Bands ?? [];
        if (bands.Length is 0 or > MaxBands)
        {
            return Problems.Reject("bands_invalid");
        }

        var clean = new List<BandRule>();
        foreach (var band in bands)
        {
            var grade = Problems.Clean(band.Grade);
            if (grade is null || grade.Length > MaxGradeLength)
            {
                return Problems.Reject("grade_invalid");
            }

            if (band.MinPercent is not { } min || min < 0 || min > 100 || decimal.Round(min, 2) != min)
            {
                return Problems.Reject("min_percent_invalid");
            }

            clean.Add(new BandRule(grade, min));
        }

        if (clean.Select(b => b.Grade.ToUpperInvariant()).Distinct().Count() != clean.Count)
        {
            return Problems.Reject("grade_duplicate");
        }

        if (clean.Select(b => b.MinPercent).Distinct().Count() != clean.Count)
        {
            return Problems.Reject("min_percent_duplicate");
        }

        // Every percentage must earn a grade, so the lowest band has to start at 0.
        if (clean.All(b => b.MinPercent != 0))
        {
            return Problems.Reject("band_zero_missing");
        }

        var scheme = await schemes.EnsureAsync(ct);
        db.GradeBands.RemoveRange(await db.GradeBands.Where(b => b.GradeSchemeId == scheme.Id).ToListAsync(ct));
        db.GradeBands.AddRange(clean.Select(b => new GradeBand { GradeSchemeId = scheme.Id, Grade = b.Grade, MinPercent = b.MinPercent }));
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(scheme, [.. clean.OrderByDescending(b => b.MinPercent)]));
    }

    private static GradeSchemeDto ToDto(GradeScheme scheme, List<BandRule> bands) =>
        new(scheme.Name, [.. bands.Select(b => new BandDto(b.Grade, b.MinPercent))]);
}
