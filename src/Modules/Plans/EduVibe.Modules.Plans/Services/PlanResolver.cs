using EduVibe.Modules.Plans.Domain;
using EduVibe.Shared.Plans;
using EduVibe.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Plans.Services;

/// <summary>Which plan a teacher is on today, with the subscription behind it when there is one.</summary>
internal sealed record ResolvedPlan(Guid? PlanId, TeacherSubscription? Subscription, TeacherPlan Plan);

/// <summary>
/// Works out a teacher's plan: the subscription in effect today, else the default plan, else the built-in Free.
/// Remembers each answer for the request, so several checks in one request cost one lookup.
/// </summary>
internal sealed class PlanResolver(PlansDbContext db, TimeProvider clock)
{
    private readonly Dictionary<Guid, ResolvedPlan> _cache = [];

    public async Task<ResolvedPlan> ResolveAsync(Guid teacherId, CancellationToken ct)
    {
        if (_cache.TryGetValue(teacherId, out var known))
        {
            return known;
        }

        var today = PlanDefaults.Today(clock);
        var subscription = await db.Subscriptions
            .Where(s => s.TeacherId == teacherId && s.Status == SubscriptionStatus.Active && s.StartsOn <= today && (s.EndsOn == null || s.EndsOn >= today))
            .OrderByDescending(s => s.StartsOn)
            .FirstOrDefaultAsync(ct);

        var plan = subscription is null
            ? await db.Plans.FirstOrDefaultAsync(p => p.IsDefault, ct)
            : await db.Plans.SingleAsync(p => p.Id == subscription.PlanId, ct);

        var resolved = plan is null
            ? new ResolvedPlan(null, null, PlanDefaults.Fallback)
            : new ResolvedPlan(plan.Id, subscription, await ViewAsync(plan, subscription?.EndsOn, ct));
        _cache[teacherId] = resolved;
        return resolved;
    }

    public async Task<TeacherPlan> ViewAsync(Plan plan, DateOnly? endsOn, CancellationToken ct)
    {
        var rows = await db.Features.Where(f => f.PlanId == plan.Id).ToListAsync(ct);
        var features = rows.Where(r => r.Enabled && PlanFeatures.All.Contains(r.Key)).Select(r => r.Key).ToHashSet();
        var limits = PlanLimits.All.ToDictionary(k => k, k => rows.FirstOrDefault(r => r.Key == k)?.LimitValue);
        return new TeacherPlan(plan.Name, endsOn, features, limits);
    }
}

internal sealed class PlanGate(PlanResolver resolver, ITenantContext tenant) : IPlanGate
{
    public Task<TeacherPlan> CurrentAsync(CancellationToken cancellationToken = default) =>
        tenant.TenantId is { } teacherId
            ? ForTeacherAsync(teacherId, cancellationToken)
            : throw new InvalidOperationException("There is no signed-in teacher to check a plan for.");

    public async Task<TeacherPlan> ForTeacherAsync(Guid teacherId, CancellationToken cancellationToken = default) =>
        (await resolver.ResolveAsync(teacherId, cancellationToken)).Plan;
}
