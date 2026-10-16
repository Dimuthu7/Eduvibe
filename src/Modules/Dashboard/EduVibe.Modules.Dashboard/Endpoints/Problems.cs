using Microsoft.AspNetCore.Http;

namespace EduVibe.Modules.Dashboard.Endpoints;

internal static class Problems
{
    /// <summary>A rejected request with a code the web app turns into a message.</summary>
    public static IResult Reject(string code, int status = StatusCodes.Status400BadRequest) =>
        Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
