using Microsoft.AspNetCore.Http;

namespace EduVibe.Modules.Fees.Endpoints;

internal static class Problems
{
    /// <summary>A rejected request with a code the web app turns into a message beside the form field.</summary>
    public static IResult Reject(string code, int status = StatusCodes.Status400BadRequest) =>
        Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Today's date in Sri Lanka (UTC+5:30, no daylight saving).</summary>
    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);
}
