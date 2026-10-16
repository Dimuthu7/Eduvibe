using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using EduVibe.Modules.Exams.Services;

namespace EduVibe.Modules.Exams.Endpoints;

/// <summary>
/// The latest exam of one class. Average is of the students whose result is complete; PreviousAverage is the same for the exam before it.
/// Marked of Students says how much of the marking is done.
/// </summary>
public sealed record ExamProgressClassDto(
    Guid ClassId, string ClassTitle, Guid ExamId, string ExamName, DateOnly HeldOn, int Students, int Marked,
    decimal? AveragePercent, decimal? PreviousAverage, bool Published);

/// <summary>A student whose percentage fell from the exam before to the latest one.</summary>
public sealed record ExamDropDto(
    Guid EnrolmentId, Guid ClassId, string ClassTitle, int IndexNumber, string FullName, string ExamName, decimal Previous, decimal Latest);

public sealed record ExamProgressDto(decimal DropPoints, ExamProgressClassDto[] Classes, int DroppedTotal, ExamDropDto[] Dropped);

/// <summary>The dashboard's exam progress: where each class stands in its latest exam, and who slipped since the one before.</summary>
internal static class ProgressEndpoints
{
    public const decimal DropPoints = 10m;
    public const int MaxClasses = 6;
    public const int MaxDropped = 8;

    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/progress", Progress).RequireAuthorization(Policies.Teacher);

    private static async Task<IResult> Progress(
        ExamsDbContext db, IClassDirectory classes, IClassRoster roster, TimeProvider clock, CancellationToken ct)
    {
        var today = Problems.Today(clock);
        var exams = (await db.Exams.AsNoTracking().Where(e => e.HeldOn <= today).ToListAsync(ct))
            .OrderByDescending(e => e.HeldOn).ThenByDescending(e => e.CreatedAt)
            .ToList();
        var titles = await classes.FindManyAsync(exams.Select(e => e.ClassId).Distinct(), ct);
        var perClass = exams
            .Where(e => titles.TryGetValue(e.ClassId, out var t) && !t.IsArchived)
            .GroupBy(e => e.ClassId)
            .Select(g => g.ToList())
            .OrderByDescending(list => list[0].HeldOn).ThenBy(list => titles[list[0].ClassId].Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxClasses)
            .ToList();
        if (perClass.Count == 0)
        {
            return Results.Ok(new ExamProgressDto(DropPoints, [], 0, []));
        }

        var examIds = perClass.SelectMany(list => list.Select(e => e.Id)).ToList();
        var results = (await db.Results.AsNoTracking().Where(r => examIds.Contains(r.ExamId))
                .Select(r => new { r.ExamId, r.EnrolmentId, r.IsAbsent, r.Percent }).ToListAsync(ct))
            .GroupBy(r => r.ExamId).ToDictionary(g => g.Key, g => g.ToList());

        decimal? Average(Guid examId) =>
            results.TryGetValue(examId, out var own) && own.Where(r => r.Percent is not null).ToList() is { Count: > 0 } scored
                ? ExamCalculator.Round(scored.Average(r => r.Percent!.Value))
                : null;

        var cards = new List<ExamProgressClassDto>();
        var drops = new List<ExamDropDto>();
        foreach (var list in perClass)
        {
            var latest = list[0];
            var people = (await roster.ActiveRosterAsync(latest.ClassId, ct)).ToDictionary(p => p.EnrolmentId);
            var own = results.GetValueOrDefault(latest.Id) ?? [];

            // The exam before is the newest older one somebody has a complete result in.
            var before = list.Skip(1).FirstOrDefault(e => results.TryGetValue(e.Id, out var rows) && rows.Any(r => r.Percent is not null));
            cards.Add(new ExamProgressClassDto(
                latest.ClassId, titles[latest.ClassId].Title, latest.Id, latest.Name, latest.HeldOn, people.Count,
                own.Count(r => people.ContainsKey(r.EnrolmentId) && (r.IsAbsent || r.Percent is not null)),
                Average(latest.Id), before is null ? null : Average(before.Id), latest.PublishedAt is not null));

            if (before is null)
            {
                continue;
            }

            var earlier = results[before.Id].Where(r => r.Percent is not null).ToDictionary(r => r.EnrolmentId, r => r.Percent!.Value);
            foreach (var r in own.Where(r => r.Percent is not null && people.ContainsKey(r.EnrolmentId) && earlier.ContainsKey(r.EnrolmentId)))
            {
                if (earlier[r.EnrolmentId] - r.Percent!.Value >= DropPoints)
                {
                    var p = people[r.EnrolmentId];
                    drops.Add(new ExamDropDto(
                        p.EnrolmentId, latest.ClassId, titles[latest.ClassId].Title, p.IndexNumber, p.FullName, latest.Name,
                        ExamCalculator.Round(earlier[r.EnrolmentId]), ExamCalculator.Round(r.Percent!.Value)));
                }
            }
        }

        var worst = drops.OrderByDescending(d => d.Previous - d.Latest).ThenBy(d => d.ClassTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.IndexNumber).Take(MaxDropped).ToArray();
        return Results.Ok(new ExamProgressDto(DropPoints, [.. cards], drops.Count, worst));
    }
}
