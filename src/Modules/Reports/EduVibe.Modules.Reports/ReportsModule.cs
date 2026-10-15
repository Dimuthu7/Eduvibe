using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Modules.Reports;

/// <summary>
/// Turns reports into Excel files, and shows the Super Admin the dashboard and the audit log. It holds no data of its own: each module that owns data offers an <see cref="IReportSource"/>,
/// and a new report is a new source, with no change here.
/// </summary>
public sealed class ReportsModule : IModule
{
    public const string ModuleName = "reports";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var reports = endpoints.MapGroup($"/api/{ModuleName}").WithTags("Reports");
        AdminEndpoints.Map(reports);
        reports.MapGet("/{key}", Download).RequireAuthorization(Policies.Teacher);
    }

    private static async Task<IResult> Download(string key, HttpRequest request, IEnumerable<IReportSource> sources, CancellationToken ct)
    {
        var source = sources.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return Problem("report_not_found", StatusCodes.Status404NotFound);
        }

        var query = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var result = await source.BuildAsync(query, ct);
        if (result.Table is null)
        {
            var code = result.Error ?? "report_invalid";
            return Problem(code, code.EndsWith("_not_found", StringComparison.Ordinal) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest);
        }

        var name = new string([.. result.Table.Title.Select(c => char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark || c is ' ' or '-' or '_' ? c : '-')]).Trim();
        return Results.File(XlsxWriter.Write(result.Table), XlsxWriter.ContentType, $"{(name.Length == 0 ? "report" : name)}.xlsx");
    }

    private static IResult Problem(string code, int status) =>
        Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
