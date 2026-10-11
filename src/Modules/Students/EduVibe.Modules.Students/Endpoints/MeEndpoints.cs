using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EduVibe.Modules.Students.Endpoints;

public sealed record MySlotDto(int Day, string Start, string End);

public sealed record MyClassDto(Guid EnrolmentId, Guid ClassId, string ClassTitle, string? TeacherName, MySlotDto[] Slots);

/// <summary>The student's own screens: their classes and weekly times, across every teacher they study with.</summary>
internal static class MeEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/me/classes", MyClasses).RequireAuthorization(Policies.Student);
    }

    private static async Task<IResult> MyClasses(
        ITenantContext user, IClassRoster roster, IClassDirectory classes, ITeacherDirectory teachers, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var mine = await roster.ActiveEnrolmentsOfUserAsync(userId, ct);
        var ids = mine.Select(m => m.ClassId).Distinct().ToList();
        var slots = await classes.SlotsInAnyTenantAsync(ids, ct);

        var result = new List<MyClassDto>();
        foreach (var enrolment in mine)
        {
            var cls = await classes.FindInAnyTenantAsync(enrolment.ClassId, ct);
            if (cls is null || cls.IsArchived)
            {
                continue;
            }

            var times = slots.TryGetValue(cls.Id, out var found) ? found : [];
            result.Add(new MyClassDto(
                enrolment.EnrolmentId, cls.Id, cls.Title, await teachers.FindNameAsync(cls.TenantId, ct),
                [.. times.Select(t => new MySlotDto(t.Day, t.Start.ToString("HH:mm"), t.End.ToString("HH:mm")))]));
        }

        return Results.Ok(result.OrderBy(c => c.ClassTitle).ToList());
    }
}
