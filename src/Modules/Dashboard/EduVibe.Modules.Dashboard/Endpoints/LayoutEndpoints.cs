using System.Text.Json;
using System.Text.RegularExpressions;
using EduVibe.Modules.Dashboard.Domain;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Dashboard.Endpoints;

public sealed record WidgetSettingDto(string? Key, bool? Enabled);

public sealed record SaveLayoutRequest(WidgetSettingDto[]? Widgets);

/// <summary>Customised is false until the teacher saves once, so the app knows to use its own default order.</summary>
public sealed record LayoutDto(WidgetSettingDto[] Widgets, bool Customised);

/// <summary>Reads and saves how the signed-in teacher arranged their dashboard.</summary>
internal static partial class LayoutEndpoints
{
    private const int MaxWidgets = 30;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex KeyPattern();

    public static void Map(RouteGroupBuilder group)
    {
        var layout = group.MapGroup("/layout").RequireAuthorization(Policies.Teacher);
        layout.MapGet("", Get);
        layout.MapPut("", Save);
        layout.MapDelete("", Reset);
    }

    private static async Task<IResult> Get(DashboardDbContext db, CancellationToken ct)
    {
        var row = await db.Layouts.AsNoTracking().SingleOrDefaultAsync(ct);
        return Results.Ok(row is null ? new LayoutDto([], false) : new LayoutDto(Read(row), true));
    }

    // The whole list replaces the saved one. Keys are only checked for shape: the web app decides which widgets exist.
    private static async Task<IResult> Save(SaveLayoutRequest request, DashboardDbContext db, CancellationToken ct)
    {
        var widgets = request.Widgets;
        if (widgets is null || widgets.Length > MaxWidgets
            || widgets.Any(w => w.Key is null || !KeyPattern().IsMatch(w.Key) || w.Enabled is null)
            || widgets.Select(w => w.Key).Distinct().Count() != widgets.Length)
        {
            return Problems.Reject("layout_invalid");
        }

        var clean = widgets.Select(w => new WidgetSettingDto(w.Key, w.Enabled)).ToArray();
        var json = JsonSerializer.Serialize(clean, Json);

        for (var attempt = 0; ; attempt++)
        {
            var row = await db.Layouts.SingleOrDefaultAsync(ct);
            if (row is null)
            {
                db.Layouts.Add(new DashboardLayout { WidgetsJson = json });
            }
            else
            {
                row.WidgetsJson = json;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(new LayoutDto(clean, true));
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Two first saves raced and the unique index stopped one; look again and update the other's row.
                db.ChangeTracker.Clear();
            }
        }
    }

    // Back to the app's default order and choices.
    private static async Task<IResult> Reset(DashboardDbContext db, CancellationToken ct)
    {
        await db.Layouts.ExecuteDeleteAsync(ct);
        return Results.Ok(new LayoutDto([], false));
    }

    private static WidgetSettingDto[] Read(DashboardLayout row)
    {
        try
        {
            return JsonSerializer.Deserialize<WidgetSettingDto[]>(row.WidgetsJson, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
