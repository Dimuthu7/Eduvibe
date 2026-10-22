using GuruBook.Modules.Plans.Domain;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Plans.Endpoints;

/// <summary>Features is on or off per key of <see cref="PlanFeatures.All"/>; Limits is a cap per key of <see cref="PlanLimits.All"/>, null for no cap.</summary>
public sealed record PlanDto(
    Guid Id, string Name, string? Description, bool IsDefault, bool IsActive, decimal MonthlyPrice,
    Dictionary<string, bool> Features, Dictionary<string, int?> Limits);

public sealed record PlanRequest(
    string? Name, string? Description, decimal? MonthlyPrice, bool? IsActive,
    Dictionary<string, bool>? Features, Dictionary<string, int?>? Limits);

/// <summary>The Super Admin's plans: what each includes and its caps.</summary>
internal static class PlanEndpoints
{
    private const decimal MaxPrice = 10_000_000m;
    private const int MaxLimit = 1_000_000;

    public static void Map(RouteGroupBuilder group)
    {
        var plans = group.MapGroup("/plans").RequireAuthorization(Policies.SuperAdmin);
        plans.MapGet("", List);
        plans.MapPost("", Create);
        plans.MapPut("/{id:guid}", Update);
    }

    private static async Task<IResult> List(PlansDbContext db, CancellationToken ct)
    {
        var plans = await db.Plans.OrderByDescending(p => p.IsDefault).ThenBy(p => p.MonthlyPrice).ThenBy(p => p.Name).ToListAsync(ct);
        var features = (await db.Features.ToListAsync(ct)).ToLookup(f => f.PlanId);
        return Results.Ok(plans.Select(p => ToDto(p, features[p.Id])).ToList());
    }

    private static async Task<IResult> Create(PlanRequest request, PlansDbContext db, CancellationToken ct)
    {
        if (await Validate(request, null, db, ct) is { } problem)
        {
            return problem;
        }

        var plan = new Plan { Name = request.Name!.Trim(), IsActive = request.IsActive ?? true };
        Apply(plan, request);
        db.Plans.Add(plan);
        var rows = PlanKeysFor(plan.Id, request);
        db.Features.AddRange(rows);
        return await SaveAsync(db, ct) ?? Results.Created($"/api/plans/plans/{plan.Id}", ToDto(plan, rows));
    }

    private static async Task<IResult> Update(Guid id, PlanRequest request, PlansDbContext db, CancellationToken ct)
    {
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null)
        {
            return Results.NotFound();
        }

        if (await Validate(request, plan, db, ct) is { } problem)
        {
            return problem;
        }

        plan.Name = request.Name!.Trim();
        plan.IsActive = request.IsActive ?? plan.IsActive;
        Apply(plan, request);

        // Every plan holds a row per key, so an edit changes rows in place and the audit log shows only what changed.
        var rows = await db.Features.Where(f => f.PlanId == id).ToListAsync(ct);
        foreach (var wanted in PlanKeysFor(id, request))
        {
            var row = rows.FirstOrDefault(r => r.Key == wanted.Key);
            if (row is null)
            {
                db.Features.Add(wanted);
                rows.Add(wanted);
                continue;
            }

            row.Enabled = wanted.Enabled;
            row.LimitValue = wanted.LimitValue;
        }

        return await SaveAsync(db, ct) ?? Results.Ok(ToDto(plan, rows));
    }

    private static void Apply(Plan plan, PlanRequest request)
    {
        plan.Description = Problems.Clean(request.Description);
        plan.MonthlyPrice = request.MonthlyPrice ?? 0m;
    }

    /// <summary>One row per key: a feature missing from the request is off, a limit missing from it has no cap.</summary>
    private static List<PlanFeature> PlanKeysFor(Guid planId, PlanRequest request)
    {
        var rows = new List<PlanFeature>();
        foreach (var key in PlanFeatures.All)
        {
            rows.Add(new PlanFeature { PlanId = planId, Key = key, Enabled = request.Features?.GetValueOrDefault(key) ?? false });
        }

        foreach (var key in PlanLimits.All)
        {
            rows.Add(new PlanFeature { PlanId = planId, Key = key, Enabled = true, LimitValue = request.Limits?.GetValueOrDefault(key) });
        }

        return rows;
    }

    private static async Task<IResult?> Validate(PlanRequest request, Plan? existing, PlansDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 60)
        {
            return Problems.Reject("name_required");
        }

        var name = request.Name.Trim().ToLower();
        var exceptId = existing?.Id ?? Guid.Empty;
        if (await db.Plans.AnyAsync(p => p.Id != exceptId && p.Name.ToLower() == name, ct))
        {
            return Problems.Reject("name_taken", StatusCodes.Status409Conflict);
        }

        if (request.Description is { } description && description.Trim().Length > 300)
        {
            return Problems.Reject("description_too_long");
        }

        if (request.MonthlyPrice is { } price && (price < 0 || price > MaxPrice))
        {
            return Problems.Reject("price_invalid");
        }

        if (request.Features?.Keys.Any(k => !PlanFeatures.All.Contains(k)) == true)
        {
            return Problems.Reject("feature_invalid");
        }

        if (request.Limits?.Any(l => !PlanLimits.All.Contains(l.Key) || l.Value is < 0 or > MaxLimit) == true)
        {
            return Problems.Reject("limit_invalid");
        }

        if (existing?.IsDefault == true && request.IsActive == false)
        {
            return Problems.Reject("default_plan_active", StatusCodes.Status409Conflict);
        }

        return null;
    }

    private static async Task<IResult?> SaveAsync(PlansDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            // Two plans made with the same name at the same moment: the unique index stops the second.
            return Problems.Reject("name_taken", StatusCodes.Status409Conflict);
        }
    }

    internal static PlanDto ToDto(Plan plan, IEnumerable<PlanFeature> rows)
    {
        var list = rows.ToList();
        return new PlanDto(
            plan.Id, plan.Name, plan.Description, plan.IsDefault, plan.IsActive, plan.MonthlyPrice,
            PlanFeatures.All.ToDictionary(k => k, k => list.Any(r => r.Key == k && r.Enabled)),
            PlanLimits.All.ToDictionary(k => k, k => list.FirstOrDefault(r => r.Key == k)?.LimitValue));
    }
}
