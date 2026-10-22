using GuruBook.Modules.Plans.Services;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Plans.Endpoints;

/// <summary>AvailableOn names the cheapest plan that includes a feature the teacher does not have, for the "Available on Pro" note.</summary>
public sealed record MyFeatureDto(string Key, bool Enabled, string? AvailableOn);

public sealed record MyLimitDto(string Key, int? Limit);

public sealed record MyPlanDto(string PlanName, DateOnly? EndsOn, MyFeatureDto[] Features, MyLimitDto[] Limits);

/// <summary>A teacher's own plan, read only. The Super Admin decides it.</summary>
internal static class MyPlanEndpoints
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/mine", Mine).RequireAuthorization(Policies.Teacher);

    private static async Task<IResult> Mine(IPlanGate gate, PlansDbContext db, CancellationToken ct)
    {
        var plan = await gate.CurrentAsync(ct);

        var locked = PlanFeatures.All.Where(k => !plan.Has(k)).ToList();
        var offers = locked.Count == 0
            ? []
            : await (from f in db.Features
                     join p in db.Plans on f.PlanId equals p.Id
                     where p.IsActive && f.Enabled && locked.Contains(f.Key)
                     select new { f.Key, p.Name, p.MonthlyPrice }).ToListAsync(ct);

        return Results.Ok(new MyPlanDto(
            plan.Name, plan.EndsOn,
            [.. PlanFeatures.All.Select(k => new MyFeatureDto(
                k, plan.Has(k), plan.Has(k) ? null : offers.Where(o => o.Key == k).OrderBy(o => o.MonthlyPrice).ThenBy(o => o.Name).Select(o => o.Name).FirstOrDefault()))],
            [.. PlanLimits.All.Select(k => new MyLimitDto(k, plan.LimitOf(k)))]));
    }
}
