using EduVibe.Modules.Plans.Domain;
using EduVibe.Modules.Plans.Services;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Plans.Endpoints;

public sealed record AssignmentDto(Guid TeacherId, Guid PlanId, string PlanName, DateOnly? EndsOn);

/// <summary>PlanId is null when the teacher is on the built-in Free because the default plan is missing.</summary>
public sealed record EffectivePlanDto(Guid? PlanId, string PlanName, bool IsDefault, DateOnly? StartsOn, DateOnly? EndsOn);

public sealed record SubscriptionDto(
    Guid Id, Guid PlanId, string PlanName, DateOnly StartsOn, DateOnly? EndsOn, string Status, string? Note, bool InEffect);

public sealed record PaymentDto(Guid Id, Guid SubscriptionId, string PlanName, decimal Amount, DateOnly PaidOn, string Method, string? Reference);

public sealed record TeacherPlanDto(
    Guid TeacherId, string TeacherName, EffectivePlanDto Effective, SubscriptionDto[] Subscriptions, PaymentDto[] Payments);

public sealed record SubscriptionRequest(Guid? PlanId, DateOnly? StartsOn, DateOnly? EndsOn, string? Note);

public sealed record PaymentRequest(decimal? Amount, DateOnly? PaidOn, string? Method, string? Reference);

/// <summary>The Super Admin puts a teacher on a plan and records what they paid.</summary>
internal static class TeacherPlanEndpoints
{
    public static readonly string[] Methods = ["cash", "bank_transfer", "other"];
    private const decimal MaxAmount = 10_000_000m;

    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/teachers").RequireAuthorization(Policies.SuperAdmin);
        admin.MapGet("/{teacherId:guid}", Get);
        admin.MapPost("/{teacherId:guid}/subscription", Assign);
        admin.MapPost("/{teacherId:guid}/payments", RecordPayment);

        group.MapGet("/assignments", Assignments).RequireAuthorization(Policies.SuperAdmin);
    }

    // The plan each teacher is on today, for the teachers list. Teachers not listed are on the default plan.
    private static async Task<IResult> Assignments(PlansDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var today = PlanDefaults.Today(clock);
        var rows = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.StartsOn <= today && (s.EndsOn == null || s.EndsOn >= today))
            .ToListAsync(ct);
        var names = await db.Plans.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return Results.Ok(rows
            .GroupBy(s => s.TeacherId)
            .Select(g => g.OrderByDescending(s => s.StartsOn).First())
            .Select(s => new AssignmentDto(s.TeacherId, s.PlanId, names[s.PlanId], s.EndsOn))
            .ToList());
    }

    private static async Task<IResult> Get(
        Guid teacherId, PlansDbContext db, PlanResolver resolver, ITeacherDirectory teachers, TimeProvider clock, CancellationToken ct)
    {
        var name = await teachers.FindNameAsync(teacherId, ct);
        return name is null ? Results.NotFound() : Results.Ok(await Build(teacherId, name, db, resolver, clock, ct));
    }

    private static async Task<IResult> Assign(
        Guid teacherId, SubscriptionRequest request, PlansDbContext db, PlanResolver resolver, ITeacherDirectory teachers, TimeProvider clock, CancellationToken ct)
    {
        var name = await teachers.FindNameAsync(teacherId, ct);
        if (name is null)
        {
            return Results.NotFound();
        }

        var plan = request.PlanId is { } planId ? await db.Plans.SingleOrDefaultAsync(p => p.Id == planId, ct) : null;
        if (plan is null || !plan.IsActive)
        {
            return Problems.Reject("plan_invalid");
        }

        var startsOn = request.StartsOn ?? PlanDefaults.Today(clock);
        if (request.EndsOn is { } endsOn && endsOn < startsOn)
        {
            return Problems.Reject("dates_invalid");
        }

        var note = Problems.Clean(request.Note);
        if (note is { Length: > 200 })
        {
            return Problems.Reject("note_too_long");
        }

        // The new plan takes over from its start date: whatever was running stops the day before, and anything that
        // would only have started on or after that date never takes effect.
        var open = await db.Subscriptions
            .Where(s => s.TeacherId == teacherId && s.Status == SubscriptionStatus.Active && (s.EndsOn == null || s.EndsOn >= startsOn))
            .ToListAsync(ct);
        foreach (var previous in open)
        {
            if (previous.StartsOn >= startsOn)
            {
                previous.Status = SubscriptionStatus.Cancelled;
            }
            else
            {
                previous.EndsOn = startsOn.AddDays(-1);
            }
        }

        db.Subscriptions.Add(new TeacherSubscription { TeacherId = teacherId, PlanId = plan.Id, StartsOn = startsOn, EndsOn = request.EndsOn, Note = note });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Build(teacherId, name, db, new PlanResolver(db, clock), clock, ct));
    }

    private static async Task<IResult> RecordPayment(
        Guid teacherId, PaymentRequest request, PlansDbContext db, PlanResolver resolver, ITeacherDirectory teachers,
        ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var name = await teachers.FindNameAsync(teacherId, ct);
        if (name is null)
        {
            return Results.NotFound();
        }

        if (request.Amount is not { } amount || amount <= 0 || amount > MaxAmount)
        {
            return Problems.Reject("amount_invalid");
        }

        var today = PlanDefaults.Today(clock);
        var paidOn = request.PaidOn ?? today;
        if (paidOn > today)
        {
            return Problems.Reject("paid_on_in_future");
        }

        var method = request.Method?.Trim().ToLowerInvariant();
        if (method is null || !Methods.Contains(method))
        {
            return Problems.Reject("method_invalid");
        }

        var reference = Problems.Clean(request.Reference);
        if (reference is { Length: > 100 })
        {
            return Problems.Reject("reference_too_long");
        }

        // A payment belongs to the subscription running now; with none running, to the latest one that has started.
        var subscription = (await resolver.ResolveAsync(teacherId, ct)).Subscription
            ?? await db.Subscriptions
                .Where(s => s.TeacherId == teacherId && s.Status == SubscriptionStatus.Active && s.StartsOn <= today)
                .OrderByDescending(s => s.StartsOn)
                .FirstOrDefaultAsync(ct);
        if (subscription is null)
        {
            return Problems.Reject("subscription_required", StatusCodes.Status409Conflict);
        }

        db.Payments.Add(new PlanPayment
        {
            SubscriptionId = subscription.Id, TeacherId = teacherId, Amount = amount, PaidOn = paidOn,
            Method = method, Reference = reference, RecordedBy = tenant.UserId,
        });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Build(teacherId, name, db, resolver, clock, ct));
    }

    private static async Task<TeacherPlanDto> Build(
        Guid teacherId, string name, PlansDbContext db, PlanResolver resolver, TimeProvider clock, CancellationToken ct)
    {
        var today = PlanDefaults.Today(clock);
        var subscriptions = await db.Subscriptions.Where(s => s.TeacherId == teacherId).OrderByDescending(s => s.StartsOn).ThenByDescending(s => s.Id).ToListAsync(ct);
        var payments = await db.Payments.Where(p => p.TeacherId == teacherId).OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.Id).ToListAsync(ct);
        var planNames = await db.Plans.ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var resolved = await resolver.ResolveAsync(teacherId, ct);
        var effective = new EffectivePlanDto(
            resolved.PlanId, resolved.Plan.Name, resolved.Subscription is null, resolved.Subscription?.StartsOn, resolved.Subscription?.EndsOn);

        return new TeacherPlanDto(
            teacherId, name, effective,
            [.. subscriptions.Select(s => new SubscriptionDto(
                s.Id, s.PlanId, planNames[s.PlanId], s.StartsOn, s.EndsOn, s.Status.ToString().ToLowerInvariant(), s.Note, s.Id == resolved.Subscription?.Id))],
            [.. payments.Select(p => new PaymentDto(
                p.Id, p.SubscriptionId, planNames[subscriptions.Single(s => s.Id == p.SubscriptionId).PlanId], p.Amount, p.PaidOn, p.Method, p.Reference))]);
    }
}
