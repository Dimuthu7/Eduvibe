using EduVibe.Modules.Plans.Domain;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Plans.Services;

/// <summary>How many active teachers are on each plan, for the Super Admin dashboard. Teachers with no subscription in effect count on the default plan.</summary>
internal sealed class PlansStats(PlansDbContext db, ITeacherDirectory teachers, TimeProvider clock) : IStatsSource
{
    public async Task<IReadOnlyList<Stat>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var today = PlanDefaults.Today(clock);
        var current = (await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active && s.StartsOn <= today && (s.EndsOn == null || s.EndsOn >= today))
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.TeacherId)
            .Select(g => g.OrderByDescending(s => s.StartsOn).First())
            .ToList();

        var active = await teachers.FindActiveAsync(current.Select(s => s.TeacherId), cancellationToken);
        var perPlan = current.Where(s => active.Contains(s.TeacherId)).GroupBy(s => s.PlanId).ToDictionary(g => g.Key, g => g.Count());
        var unassigned = await teachers.CountActiveAsync(cancellationToken) - perPlan.Values.Sum();

        var plans = await db.Plans.OrderByDescending(p => p.IsDefault).ThenBy(p => p.MonthlyPrice).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        return [.. plans.Select(p => new Stat($"plan:{p.Id}", perPlan.GetValueOrDefault(p.Id) + (p.IsDefault ? Math.Max(unassigned, 0) : 0), p.Name))];
    }
}
