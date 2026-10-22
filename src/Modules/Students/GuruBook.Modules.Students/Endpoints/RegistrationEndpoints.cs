using GuruBook.Modules.Students.Domain;
using GuruBook.Shared.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students.Endpoints;

public sealed record InviteInfoDto(string ClassTitle, string? TeacherName, DateTimeOffset ExpiresAt);

public sealed record RegisterRequest(
    string? Phone, string? FirstName, string? LastName, string? School, string? Grade, string? GuardianName, string? GuardianPhone, string? Password);

/// <summary>Outcome is "created" (a new account with the chosen password) or "existing_account" (the phone number already had one).</summary>
public sealed record RegisteredDto(string Outcome, string ClassTitle);

/// <summary>
/// The pages a student opens from an invite link. Nobody is signed in, so the class and its teacher come from the
/// link itself. The student then waits as Pending until the teacher approves.
/// </summary>
internal static class RegistrationEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var register = group.MapGroup("/register/{token}").AllowAnonymous().RequireRateLimiting("auth");
        register.MapGet("", Info);
        register.MapPost("", Submit);
    }

    private static async Task<IResult> Info(
        string token, StudentsDbContext db, IClassDirectory classes, ITeacherDirectory teachers, TimeProvider clock, CancellationToken ct)
    {
        var (invite, cls, problem) = await Resolve(token, db, classes, clock, ct);
        if (invite is null || cls is null)
        {
            return problem!;
        }

        return Results.Ok(new InviteInfoDto(cls.Title, await teachers.FindNameAsync(cls.TenantId, ct), invite.ExpiresAt));
    }

    private static async Task<IResult> Submit(
        string token, RegisterRequest request, StudentsDbContext db, IClassDirectory classes, IAccountProvisioner accounts,
        INotifier notifier, TimeProvider clock, CancellationToken ct)
    {
        var (invite, cls, problem) = await Resolve(token, db, classes, clock, ct);
        if (invite is null || cls is null)
        {
            return problem!;
        }

        var (clean, error) = StudentRules.Validate(
            request.Phone, request.FirstName, request.LastName, request.School, request.Grade,
            request.GuardianName, request.GuardianPhone, needPhone: true);
        if (clean is null)
        {
            return Problems.Reject(error!);
        }

        if ((request.Password ?? "").Length < accounts.MinimumPasswordLength)
        {
            return Problems.Reject("password_too_short");
        }

        var student = await db.Students.SingleOrDefaultAsync(s => s.Phone == clean.Phone, ct);
        var newStudent = student is null;
        if (student is null)
        {
            // The password the student chose is kept as a hash and becomes their sign-in at once; the teacher only approves the class.
            var account = await accounts.CreateStudentAccountAsync(
                new NewStudentAccount(clean.Phone, clean.FirstName, clean.LastName, accounts.HashPassword(request.Password!)),
                linkExisting: false, ct);
            if (account.Outcome == AccountOutcome.PhoneInUse)
            {
                return Problems.Reject("phone_taken", StatusCodes.Status409Conflict);
            }

            student = new Student
            {
                Phone = clean.Phone, FirstName = clean.FirstName, LastName = clean.LastName, School = clean.School,
                Grade = clean.Grade, GuardianName = clean.GuardianName, GuardianPhone = clean.GuardianPhone, UserId = account.UserId,
            };
            db.Students.Add(student);
        }

        // For a phone number that already has a record nothing is overwritten and no password is set:
        // a link must never let a stranger take over somebody else's account.
        var (enrolment, enrolProblem) = await StudentEndpoints.EnrolAsync(
            db, student, newStudent, cls, null, FeePlan.Full, 0m, EnrolmentStatus.Pending, clock, ct, ignoreTenant: true);
        if (enrolment is null)
        {
            // Already in this class (or already waiting): the same answer as a fresh request, so nothing is revealed.
            return enrolProblem is not null && !newStudent
                ? Results.Ok(new RegisteredDto("existing_account", cls.Title))
                : enrolProblem!;
        }

        invite.UsedCount++;
        if (await StudentEndpoints.SaveAsync(db, ct) is { } failed)
        {
            return failed;
        }

        // The teacher is told once for each class while they have not looked: "3 students asked to join", not three lines.
        await notifier.NotifyAsync(
        [
            new NotifyRequest(
                cls.TenantId, "join_request", new { classTitle = cls.Title, student = student.FullName }, Link: "/students",
                GroupKey: $"join:{cls.Id}"),
        ], ct);
        return Results.Ok(new RegisteredDto(newStudent ? "created" : "existing_account", cls.Title));
    }

    private static async Task<(InviteLink? Invite, ClassSummary? Class, IResult? Problem)> Resolve(
        string token, StudentsDbContext db, IClassDirectory classes, TimeProvider clock, CancellationToken ct)
    {
        var invite = await db.InviteLinks.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.Token == token, ct);
        if (invite is null)
        {
            return (null, null, Problems.Reject("invite_not_found", StatusCodes.Status404NotFound));
        }

        if (InviteEndpoints.Problem(invite, clock.GetUtcNow()) is { } code)
        {
            return (null, null, Problems.Reject(code, StatusCodes.Status410Gone));
        }

        var cls = await classes.FindInAnyTenantAsync(invite.ClassId, ct);
        if (cls is null || cls.IsArchived)
        {
            return (null, null, Problems.Reject("invite_expired", StatusCodes.Status410Gone));
        }

        return (invite, cls, null);
    }
}
