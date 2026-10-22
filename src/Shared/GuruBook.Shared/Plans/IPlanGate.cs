using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Shared.Plans;

/// <summary>What a teacher's plan lets them do right now. A limit of null means no cap.</summary>
public sealed record TeacherPlan(
    string Name, DateOnly? EndsOn, IReadOnlySet<string> Features, IReadOnlyDictionary<string, int?> Limits)
{
    public bool Has(string feature) => Features.Contains(feature);

    public int? LimitOf(string key) => Limits.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// Answers what a teacher's plan includes. The Plans module implements it; any module that gates a feature or enforces a limit uses it
/// instead of reading plan tables.
/// </summary>
public interface IPlanGate
{
    /// <summary>The plan of the signed-in teacher.</summary>
    Task<TeacherPlan> CurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>The plan of any teacher, for background jobs and for checks made on a teacher's behalf.</summary>
    Task<TeacherPlan> ForTeacherAsync(Guid teacherId, CancellationToken cancellationToken = default);
}

public static class PlanGateExtensions
{
    /// <summary>Rejects the request with 403 and code <c>plan_feature_locked</c> unless the teacher's plan includes the feature.</summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string feature) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new PlanFeatureFilter(feature));
}

/// <summary>The check behind <see cref="PlanGateExtensions.RequireFeature"/>.</summary>
public sealed class PlanFeatureFilter(string feature) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var plan = await context.HttpContext.RequestServices.GetRequiredService<IPlanGate>().CurrentAsync(context.HttpContext.RequestAborted);
        return plan.Has(feature)
            ? await next(context)
            : Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "plan_feature_locked",
                extensions: new Dictionary<string, object?> { ["code"] = "plan_feature_locked", ["feature"] = feature });
    }
}
