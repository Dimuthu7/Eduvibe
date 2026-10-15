using EduVibe.Shared.Plans;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Shared.Tests;

public class PlanGateTests
{
    private sealed class FixedGate(params string[] features) : IPlanGate
    {
        private readonly TeacherPlan _plan = new("Test", null, features.ToHashSet(), new Dictionary<string, int?>());

        public Task<TeacherPlan> CurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(_plan);

        public Task<TeacherPlan> ForTeacherAsync(Guid teacherId, CancellationToken cancellationToken = default) => Task.FromResult(_plan);
    }

    private static async Task<(object? Result, bool ReachedEndpoint)> Run(IPlanGate gate, string feature)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(gate).BuildServiceProvider() };
        var reached = false;
        var result = await new PlanFeatureFilter(feature).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http), _ => { reached = true; return ValueTask.FromResult<object?>("ok"); });
        return (result, reached);
    }

    [Fact]
    public async Task A_plan_with_the_feature_reaches_the_endpoint()
    {
        var (result, reached) = await Run(new FixedGate(PlanFeatures.WhatsApp), PlanFeatures.WhatsApp);

        Assert.True(reached);
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task A_plan_without_the_feature_is_turned_back_with_403_and_a_code()
    {
        var (result, reached) = await Run(new FixedGate(PlanFeatures.DriveMaterials), PlanFeatures.WhatsApp);

        Assert.False(reached);
        var problem = Assert.IsType<IResult>(result, exactMatch: false);
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();
        await problem.ExecuteAsync(http);
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        Assert.Contains("plan_feature_locked", await new StreamReader(http.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public void A_limit_that_is_not_set_has_no_cap()
    {
        var plan = new TeacherPlan("Free", null, new HashSet<string>(), new Dictionary<string, int?> { [PlanLimits.MaxClasses] = 3 });

        Assert.Equal(3, plan.LimitOf(PlanLimits.MaxClasses));
        Assert.Null(plan.LimitOf(PlanLimits.MaxStudentsPerClass));
    }
}
