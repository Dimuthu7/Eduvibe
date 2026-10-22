using GuruBook.Modules.Students.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students.Endpoints;

/// <summary>OneTimePassword is set only when the parent had no account yet; a parent who already signs in keeps their own password.</summary>
public sealed record ParentInvitedDto(string Name, string Phone, bool AccountCreated, string? OneTimePassword);

public sealed record ChildClassDto(Guid EnrolmentId, Guid ClassId, string ClassTitle, string? TeacherName, bool Locked, MySlotDto[] Slots);

public sealed record ChildDto(Guid StudentId, string FirstName, string FullName, ChildClassDto[] Classes);

/// <summary>
/// The parent portal. A teacher links the guardian's phone to a student (which makes the Parent account when there is none), and the
/// parent then sees that child's classes with this teacher. The link is the only way in: the guardian phone on a student never opens anything.
/// </summary>
internal static class ParentEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var teacher = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        teacher.MapPost("/{id:guid}/parent", Invite).RequireFeature(PlanFeatures.ParentPortal);
        teacher.MapPost("/{id:guid}/parent/reset-password", ResetPassword);
        teacher.MapDelete("/{id:guid}/parent", Remove);
        group.MapGet("/family", Family).RequireAuthorization(Policies.Parent);
    }

    private static async Task<IResult> Invite(Guid id, StudentsDbContext db, IAccountProvisioner accounts, ITenantContext tenant, CancellationToken ct)
    {
        // Only a teacher who has the student in a class may invite their parent.
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct) || tenant.TenantId is not { } tenantId)
        {
            return Results.NotFound();
        }

        var student = await db.Students.SingleAsync(s => s.Id == id, ct);
        if (student.GuardianPhone is null)
        {
            return Problems.Reject("guardian_phone_missing");
        }

        if (student.GuardianPhone == student.Phone)
        {
            return Problems.Reject("guardian_phone_is_student");
        }

        var (first, last) = SplitName(student);
        var account = await accounts.CreateParentAccountAsync(new NewParentAccount(student.GuardianPhone, first, last), ct);
        if (account.UserId is not { } userId)
        {
            return Problems.Reject("conflict", StatusCodes.Status409Conflict);
        }

        if (!await db.ParentLinks.AnyAsync(l => l.StudentId == id && l.ParentUserId == userId, ct))
        {
            db.ParentLinks.Add(new ParentLink { TenantId = tenantId, StudentId = id, ParentUserId = userId });
            if (await StudentEndpoints.SaveAsync(db, ct) is { } conflict)
            {
                return conflict;
            }
        }

        return Results.Ok(new ParentInvitedDto(student.GuardianName ?? $"{first} {last}".Trim(), student.GuardianPhone, account.Outcome == AccountOutcome.Created, account.OneTimePassword));
    }

    private static async Task<IResult> ResetPassword(Guid id, StudentsDbContext db, IAccountProvisioner accounts, CancellationToken ct)
    {
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        var userId = await db.ParentLinks.Where(l => l.StudentId == id).Select(l => (Guid?)l.ParentUserId).FirstOrDefaultAsync(ct);
        var otp = userId is null ? null : await accounts.ResetParentPasswordAsync(userId.Value, ct);
        return otp is null ? Problems.Reject("account_not_resettable", StatusCodes.Status409Conflict) : Results.Ok(new OneTimePasswordDto(otp));
    }

    private static async Task<IResult> Remove(Guid id, StudentsDbContext db, IAccountProvisioner accounts, CancellationToken ct)
    {
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        await DropLinksAsync(db, accounts, id, allTeachers: false, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Removes this teacher's links for a student, or every teacher's when the guardian itself has changed. A parent with no link left anywhere stops being a parent, and their account goes when
    /// nothing else is on it, so no sign-in is left behind for a family no teacher shares anything with.
    /// </summary>
    internal static async Task DropLinksAsync(StudentsDbContext db, IAccountProvisioner accounts, Guid studentId, bool allTeachers, CancellationToken ct)
    {
        var source = allTeachers ? db.ParentLinks.IgnoreQueryFilters() : db.ParentLinks;
        var links = await source.Where(l => l.StudentId == studentId).ToListAsync(ct);
        if (links.Count == 0)
        {
            return;
        }

        db.ParentLinks.RemoveRange(links);
        await db.SaveChangesAsync(ct);
        await ReleaseParentsAsync(db, accounts, [.. links.Select(l => l.ParentUserId)], ct);
    }

    /// <summary>Takes the Parent role off the accounts among <paramref name="parentUserIds"/> that no teacher links to a child any more.</summary>
    internal static async Task ReleaseParentsAsync(StudentsDbContext db, IAccountProvisioner accounts, IEnumerable<Guid> parentUserIds, CancellationToken ct)
    {
        var ids = parentUserIds.Distinct().ToList();
        var stillLinked = await db.ParentLinks.IgnoreQueryFilters().Where(l => ids.Contains(l.ParentUserId)).Select(l => l.ParentUserId).Distinct().ToListAsync(ct);
        foreach (var userId in ids.Except(stillLinked))
        {
            await accounts.RemoveParentAccountAsync(userId, ct);
        }
    }

    // The children a parent can see, with the classes of each. A class whose teacher's plan has no parent portal is listed but locked, so
    // the parent knows why nothing shows for it; its data is not sent.
    private static async Task<IResult> Family(
        ITenantContext user, StudentsDbContext db, IClassDirectory classes, ITeacherDirectory teachers, IPlanGate plans, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var links = await db.ParentLinks.IgnoreQueryFilters().AsNoTracking().Where(l => l.ParentUserId == userId).ToListAsync(ct);
        var studentIds = links.Select(l => l.StudentId).Distinct().ToList();
        var linked = links.Select(l => (l.StudentId, l.TenantId)).ToHashSet();
        var enrolments = (await db.Enrolments.IgnoreQueryFilters().AsNoTracking().Include(e => e.Student)
                .Where(e => e.Status == EnrolmentStatus.Active && studentIds.Contains(e.StudentId)).ToListAsync(ct))
            .Where(e => linked.Contains((e.StudentId, e.TenantId)))
            .ToList();

        var slots = await classes.SlotsInAnyTenantAsync(enrolments.Select(e => e.ClassId).Distinct(), ct);
        var allowed = new Dictionary<Guid, bool>();
        var teacherNames = new Dictionary<Guid, string?>();
        var children = new List<ChildDto>();
        foreach (var group in enrolments.GroupBy(e => e.StudentId))
        {
            var rows = new List<ChildClassDto>();
            foreach (var enrolment in group)
            {
                var cls = await classes.FindInAnyTenantAsync(enrolment.ClassId, ct);
                if (cls is null || cls.IsArchived)
                {
                    continue;
                }

                if (!allowed.TryGetValue(enrolment.TenantId, out var ok))
                {
                    allowed[enrolment.TenantId] = ok = (await plans.ForTeacherAsync(enrolment.TenantId, ct)).Has(PlanFeatures.ParentPortal);
                    teacherNames[enrolment.TenantId] = await teachers.FindNameAsync(enrolment.TenantId, ct);
                }

                var times = slots.TryGetValue(cls.Id, out var found) ? found : [];
                rows.Add(new ChildClassDto(
                    enrolment.Id, cls.Id, cls.Title, teacherNames[enrolment.TenantId], !ok,
                    [.. times.Select(t => new MySlotDto(t.Day, t.Start.ToString("HH:mm"), t.End.ToString("HH:mm")))]));
            }

            if (rows.Count > 0)
            {
                var student = group.First().Student;
                children.Add(new ChildDto(student.Id, student.FirstName, student.FullName, [.. rows.OrderBy(r => r.ClassTitle)]));
            }
        }

        return Results.Ok(children.OrderBy(c => c.FullName).ToList());
    }

    // The guardian's name as the teacher wrote it ("Mrs K. Perera"), split at the first space; without one the account is "Parent of ...".
    private static (string First, string Last) SplitName(Student student)
    {
        var name = student.GuardianName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return ("Parent", student.LastName);
        }

        var space = name.IndexOf(' ');
        return space < 0 ? (name, student.LastName) : (name[..space], name[(space + 1)..].Trim());
    }
}
