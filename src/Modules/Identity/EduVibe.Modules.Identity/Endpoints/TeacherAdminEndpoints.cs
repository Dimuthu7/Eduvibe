using EduVibe.Modules.Identity.Domain;
using EduVibe.Modules.Identity.Services;
using EduVibe.Shared.Phone;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static EduVibe.Modules.Identity.Endpoints.IdentityEndpoints;

namespace EduVibe.Modules.Identity.Endpoints;

/// <summary>Super Admin: create teachers, hand out one-time passwords, switch accounts on and off.</summary>
internal static class TeacherAdminEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var teachers = group.MapGroup("/teachers").RequireAuthorization(Policies.SuperAdmin);
        teachers.MapGet("", List);
        teachers.MapPost("", Create);
        teachers.MapPost("/{id:guid}/reset-password", ResetPassword);
        teachers.MapPut("/{id:guid}/active", SetActive);
    }

    private static async Task<IResult> List(IdentityDbContext db, CancellationToken ct)
    {
        var rows = await (
            from t in db.Teachers
            join u in db.Users on t.UserId equals u.Id
            orderby u.FullName
            select new TeacherDto(t.Id, u.FullName, u.Phone, u.Email, t.Town, t.Subjects, u.IsActive, u.MustChangePassword))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> Create(CreateTeacherRequest request, IdentityDbContext db, PasswordService passwords, CancellationToken ct)
    {
        var name = request.FullName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120)
        {
            return Problem(StatusCodes.Status400BadRequest, "name_required");
        }

        var phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "phone_invalid");
        }

        if (await db.Users.AnyAsync(u => u.Phone == phone, ct))
        {
            return Problem(StatusCodes.Status409Conflict, "phone_taken");
        }

        var otp = PasswordService.NewOneTimePassword();
        var user = new User { Phone = phone, FullName = name, Email = Clean(request.Email), MustChangePassword = true };
        user.PasswordHash = passwords.Hash(user, otp);
        user.Roles.Add(new UserRole { Role = Roles.Teacher });
        var teacher = new Teacher { UserId = user.Id, Town = Clean(request.Town), Subjects = Clean(request.Subjects) };
        db.Users.Add(user);
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync(ct);

        var dto = new TeacherDto(teacher.Id, user.FullName, user.Phone, user.Email, teacher.Town, teacher.Subjects, user.IsActive, true);
        return Results.Created($"/api/identity/teachers/{teacher.Id}", new TeacherCreatedDto(dto, otp));
    }

    private static async Task<IResult> ResetPassword(Guid id, IdentityDbContext db, PasswordService passwords, AuthService auth, CancellationToken ct)
    {
        var user = await UserOfTeacher(db, id, ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        var otp = PasswordService.NewOneTimePassword();
        user.PasswordHash = passwords.Hash(user, otp);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await auth.RevokeAllAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new OneTimePasswordDto(otp));
    }

    private static async Task<IResult> SetActive(Guid id, SetActiveRequest request, IdentityDbContext db, AuthService auth, CancellationToken ct)
    {
        var user = await UserOfTeacher(db, id, ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        user.IsActive = request.IsActive;
        if (!request.IsActive)
        {
            await auth.RevokeAllAsync(user.Id, ct);
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Task<User?> UserOfTeacher(IdentityDbContext db, Guid teacherId, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => db.Teachers.Any(t => t.Id == teacherId && t.UserId == u.Id), ct);
}
