using EduVibe.Modules.Identity.Domain;
using EduVibe.Modules.Identity.Services;
using EduVibe.Shared.Phone;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Identity.Endpoints;

internal static class IdentityEndpoints
{
    public const string AuthRateLimit = "auth";

    public static void Map(RouteGroupBuilder group)
    {
        var auth = group.MapGroup("").AllowAnonymous().RequireRateLimiting(AuthRateLimit);
        auth.MapPost("/login", Login);
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", Logout);

        group.MapPost("/change-password", ChangePassword).RequireAuthorization(Policies.SignedIn);
        group.MapGet("/me", GetMe).RequireAuthorization(Policies.SignedIn);
        group.MapPut("/me", UpdateMe);
    }

    private static async Task<IResult> Login(LoginRequest request, AuthService auth, IdentityDbContext db, CancellationToken ct)
    {
        var phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null || string.IsNullOrEmpty(request.Password))
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_request");
        }

        var (tokens, error) = await auth.LoginAsync(phone, request.Password, ct);
        if (tokens is null)
        {
            return error == "locked"
                ? Problem(StatusCodes.Status429TooManyRequests, "locked")
                : Problem(StatusCodes.Status401Unauthorized, "invalid_credentials");
        }

        return Results.Ok(await ToSession(db, tokens, phone, ct));
    }

    private static async Task<IResult> Refresh(RefreshRequest request, AuthService auth, IdentityDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_request");
        }

        var tokens = await auth.RefreshAsync(request.RefreshToken, ct);
        if (tokens is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "invalid_refresh_token");
        }

        var sub = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(tokens.AccessToken).Subject;
        var user = await LoadUser(db, Guid.Parse(sub), ct);
        return Results.Ok(new SessionDto(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAt, user!));
    }

    private static async Task<IResult> Logout(RefreshRequest request, AuthService auth, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            await auth.LogoutAsync(request.RefreshToken, ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request, ITenantContext tenant, IdentityDbContext db, PasswordService passwords, AuthService auth, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || (request.NewPassword?.Length ?? 0) < PasswordService.MinimumLength)
        {
            return Problem(StatusCodes.Status400BadRequest, "password_too_short");
        }

        var user = await db.Users.Include(u => u.Roles).SingleAsync(u => u.Id == tenant.UserId, ct);
        if (!passwords.Verify(user, request.CurrentPassword))
        {
            return Problem(StatusCodes.Status400BadRequest, "wrong_password");
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return Problem(StatusCodes.Status400BadRequest, "password_unchanged");
        }

        user.PasswordHash = passwords.Hash(user, request.NewPassword!);
        user.MustChangePassword = false;
        await auth.RevokeAllAsync(user.Id, ct);
        var tokens = await auth.IssueAsync(user, ct);
        return Results.Ok(await ToSession(db, tokens, user.Phone, ct));
    }

    private static async Task<IResult> GetMe(ITenantContext tenant, IdentityDbContext db, CancellationToken ct)
    {
        var user = await LoadUser(db, tenant.UserId!.Value, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }

    private static async Task<IResult> UpdateMe(UpdateProfileRequest request, ITenantContext tenant, IdentityDbContext db, CancellationToken ct)
    {
        var name = request.FullName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120)
        {
            return Problem(StatusCodes.Status400BadRequest, "name_required");
        }

        if (request.Language is not (null or "en" or "si" or "ta"))
        {
            return Problem(StatusCodes.Status400BadRequest, "language_unsupported");
        }

        var user = await db.Users.SingleAsync(u => u.Id == tenant.UserId, ct);
        user.FullName = name;
        user.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        user.Language = request.Language ?? user.Language;

        var teacher = await db.Teachers.SingleOrDefaultAsync(t => t.UserId == user.Id, ct);
        if (teacher is not null)
        {
            teacher.Town = Clean(request.Town);
            teacher.Subjects = Clean(request.Subjects);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadUser(db, user.Id, ct));
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IResult Problem(int status, string code) =>
        Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static async Task<SessionDto> ToSession(IdentityDbContext db, TokenPair tokens, string phone, CancellationToken ct)
    {
        var id = await db.Users.Where(u => u.Phone == phone).Select(u => u.Id).SingleAsync(ct);
        return new SessionDto(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAt, (await LoadUser(db, id, ct))!);
    }

    private static async Task<UserDto?> LoadUser(IdentityDbContext db, Guid userId, CancellationToken ct)
    {
        var row = await (
            from u in db.Users
            where u.Id == userId
            select new
            {
                u.Id, u.Phone, u.FullName, u.Email, u.Language, u.MustChangePassword,
                Roles = u.Roles.Select(r => r.Role).ToList(),
                Teacher = db.Teachers.Where(t => t.UserId == u.Id).Select(t => new { t.Id, t.Town, t.Subjects }).FirstOrDefault(),
            }).SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : new UserDto(row.Id, row.Phone, row.FullName, row.Email, row.Language, [.. row.Roles],
                row.MustChangePassword, row.Teacher?.Id, row.Teacher?.Town, row.Teacher?.Subjects);
    }
}
