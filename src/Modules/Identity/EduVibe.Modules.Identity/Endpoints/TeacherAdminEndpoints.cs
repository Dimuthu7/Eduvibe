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
            orderby u.FirstName, u.LastName
            select new
            {
                t.Id, u.FirstName, u.LastName, u.Phone, u.Email, t.District, t.StreamId,
                SubjectIds = t.Subjects.Select(s => s.SubjectId).ToList(),
                u.IsActive, u.MustChangePassword,
            }).ToListAsync(ct);

        return Results.Ok(rows.Select(r => new TeacherDto(
            r.Id, r.FirstName, r.LastName, $"{r.FirstName} {r.LastName}".Trim(), r.Phone, r.Email,
            r.District, r.StreamId, [.. r.SubjectIds], r.IsActive, r.MustChangePassword)).ToList());
    }

    private static async Task<IResult> Create(
        CreateTeacherRequest request, IdentityDbContext db, PasswordService passwords, TeacherProfileRules rules, CancellationToken ct)
    {
        var phone = PhoneNumber.Normalize(request.Phone);
        if (phone is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "phone_invalid");
        }

        var (profile, error) = await rules.ValidateAsync(
            request.FirstName, request.LastName, request.District, request.StreamId, request.SubjectIds, ct);
        if (profile is null)
        {
            return Problem(StatusCodes.Status400BadRequest, error!);
        }

        if (await db.Users.AnyAsync(u => u.Phone == phone, ct))
        {
            return Problem(StatusCodes.Status409Conflict, "phone_taken");
        }

        var otp = PasswordService.NewOneTimePassword();
        var user = new User
        {
            Phone = phone, FirstName = profile.FirstName, LastName = profile.LastName,
            Email = Clean(request.Email), MustChangePassword = true,
        };
        user.PasswordHash = passwords.Hash(user, otp);
        user.Roles.Add(new UserRole { Role = Roles.Teacher });
        var teacher = new Teacher { UserId = user.Id, District = profile.District, StreamId = profile.StreamId };
        teacher.Subjects.AddRange(profile.SubjectIds.Select(id => new TeacherSubject { SubjectId = id }));
        db.Users.Add(user);
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync(ct);

        var dto = new TeacherDto(
            teacher.Id, user.FirstName, user.LastName, user.FullName, user.Phone, user.Email,
            teacher.District, teacher.StreamId, [.. profile.SubjectIds], user.IsActive, true);
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
