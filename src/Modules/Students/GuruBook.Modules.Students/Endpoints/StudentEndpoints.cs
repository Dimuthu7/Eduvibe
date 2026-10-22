using GuruBook.Modules.Students.Domain;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Phone;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Students.Endpoints;

public sealed record AddStudentRequest(
    string? Phone, string? FirstName, string? LastName, string? School, string? Grade, string? GuardianName, string? GuardianPhone,
    Guid? ClassId, int? IndexNumber, string? FeePlan, decimal? DiscountAmount, bool? EnrolExisting);

public sealed record UpdateStudentRequest(
    string? FirstName, string? LastName, string? School, string? Grade, string? GuardianName, string? GuardianPhone);

public sealed record EnrolRequest(Guid? ClassId, int? IndexNumber, string? FeePlan, decimal? DiscountAmount);

public sealed record UpdateEnrolmentRequest(int? IndexNumber, string? Status, string? FeePlan, decimal? DiscountAmount);

/// <summary>One enrolment with its student, the row a class list or the pending list shows.</summary>
public sealed record StudentRowDto(
    Guid EnrolmentId, Guid StudentId, Guid ClassId, string ClassTitle, int IndexNumber,
    string FirstName, string LastName, string FullName, string Phone, string? School, string? Grade,
    string Status, string FeePlan, decimal DiscountAmount, DateOnly JoinedOn);

public sealed record StudentCreatedDto(StudentRowDto Row, string? OneTimePassword);

public sealed record EnrolmentDto(
    Guid Id, Guid ClassId, string ClassTitle, int IndexNumber, string Status, string FeePlan, decimal DiscountAmount, DateOnly JoinedOn);

public sealed record StudentDto(
    Guid Id, string FirstName, string LastName, string FullName, string Phone, string? School, string? Grade,
    string? GuardianName, string? GuardianPhone, bool HasAccount, bool HasParent, EnrolmentDto[] Enrolments);

public sealed record OneTimePasswordDto(string OneTimePassword);

internal static class StudentEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var students = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        students.MapGet("", List);
        students.MapPost("", Add);
        students.MapGet("/{id:guid}", Get);
        students.MapPut("/{id:guid}", Update);
        students.MapPost("/{id:guid}/enrolments", Enrol);
        students.MapPost("/{id:guid}/reset-password", ResetPassword);
        students.MapPut("/enrolments/{id:guid}", UpdateEnrolment);
        students.MapPost("/enrolments/{id:guid}/approve", Approve);
        students.MapPost("/enrolments/{id:guid}/reject", Reject);
    }

    // status: current (pending and active, the default), pending, active, left or all. q matches a name, an index number or a phone.
    private static async Task<IResult> List(
        Guid? classId, string? status, string? q, StudentsDbContext db, IClassDirectory classes, CancellationToken ct)
    {
        var query = db.Enrolments.Include(e => e.Student).AsQueryable();
        if (classId is not null)
        {
            query = query.Where(e => e.ClassId == classId);
        }

        query = status?.ToLowerInvariant() switch
        {
            "pending" => query.Where(e => e.Status == EnrolmentStatus.Pending),
            "active" => query.Where(e => e.Status == EnrolmentStatus.Active),
            "left" => query.Where(e => e.Status == EnrolmentStatus.Left),
            "all" => query,
            _ => query.Where(e => e.Status != EnrolmentStatus.Left),
        };

        if (Problems.Clean(q)?.ToLowerInvariant() is { } text)
        {
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            var asNumber = int.TryParse(text, out var number) ? number : -1;
            query = query.Where(e =>
                (e.Student.FirstName + " " + e.Student.LastName).ToLower().Contains(text)
                || e.IndexNumber == asNumber
                || (digits.Length >= 3 && e.Student.Phone.Contains(digits)));
        }

        var rows = await query.OrderBy(e => e.Student.FirstName).ThenBy(e => e.Student.LastName).ToListAsync(ct);
        var titles = await classes.FindManyAsync(rows.Select(r => r.ClassId), ct);
        return Results.Ok(rows.Select(e => ToRow(e, titles)).ToList());
    }

    private static async Task<IResult> Get(Guid id, StudentsDbContext db, IClassDirectory classes, CancellationToken ct)
    {
        var enrolments = await db.Enrolments.Include(e => e.Student).Where(e => e.StudentId == id).ToListAsync(ct);
        if (enrolments.Count == 0)
        {
            return Results.NotFound();
        }

        var titles = await classes.FindManyAsync(enrolments.Select(e => e.ClassId), ct);
        var hasParent = await db.ParentLinks.AnyAsync(l => l.StudentId == id, ct);
        return Results.Ok(ToStudent(enrolments[0].Student, enrolments, titles, hasParent));
    }

    private static async Task<IResult> Add(
        AddStudentRequest request, StudentsDbContext db, IClassDirectory classes, IAccountProvisioner accounts,
        IPlanGate plans, TimeProvider clock, CancellationToken ct)
    {
        var (clean, error) = StudentRules.Validate(
            request.Phone, request.FirstName, request.LastName, request.School, request.Grade,
            request.GuardianName, request.GuardianPhone, needPhone: true);
        if (clean is null)
        {
            return Problems.Reject(error!);
        }

        var (cls, classProblem) = await FindOpenClass(request.ClassId, classes, ct);
        if (cls is null)
        {
            return classProblem!;
        }

        var plan = StudentRules.ParseFeePlan(request.FeePlan);
        if (plan is null)
        {
            return Problems.Reject("fee_plan_invalid");
        }

        var (discount, discountError) = StudentRules.CheckDiscount(plan.Value, request.DiscountAmount, cls.MonthlyFee);
        if (discount is null)
        {
            return Problems.Reject(discountError!);
        }

        // Checked before anything is saved: the sign-in account is created below and must not outlive a refused add.
        if (await ClassIsFull(db, plans, cls, ct))
        {
            return Problems.Reject("plan_limit_students", StatusCodes.Status403Forbidden);
        }

        var student = await db.Students.SingleOrDefaultAsync(s => s.Phone == clean.Phone, ct);
        string? otp = null;
        var newStudent = student is null;
        if (student is not null)
        {
            if (request.EnrolExisting != true)
            {
                var enrolledHere = await db.Enrolments.AnyAsync(e => e.StudentId == student.Id && e.ClassId == cls.Id, ct);
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict, title: "student_exists",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "student_exists",
                        ["existing"] = new { firstName = student.FirstName, lastInitial = student.LastName[..1], enrolledHere },
                    });
            }
        }
        else
        {
            var account = await accounts.CreateStudentAccountAsync(
                new NewStudentAccount(clean.Phone, clean.FirstName, clean.LastName, null), linkExisting: true, ct);
            otp = account.OneTimePassword;
            student = new Student
            {
                Phone = clean.Phone, FirstName = clean.FirstName, LastName = clean.LastName, School = clean.School,
                Grade = clean.Grade, GuardianName = clean.GuardianName, GuardianPhone = clean.GuardianPhone, UserId = account.UserId,
            };
            db.Students.Add(student);
        }

        var (enrolment, problem) = await EnrolAsync(db, student, newStudent, cls, request.IndexNumber, plan.Value, discount.Value, EnrolmentStatus.Active, clock, ct);
        if (enrolment is null)
        {
            return problem!;
        }

        return await SaveAsync(db, ct) ?? Results.Created(
            $"/api/students/{student.Id}", new StudentCreatedDto(ToRow(enrolment, TitlesOf(cls)), otp));
    }

    private static async Task<IResult> Update(Guid id, UpdateStudentRequest request, StudentsDbContext db, IAccountProvisioner accounts, CancellationToken ct)
    {
        // Only a teacher who has the student in a class may edit them.
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        var (clean, error) = StudentRules.Validate(
            null, request.FirstName, request.LastName, request.School, request.Grade,
            request.GuardianName, request.GuardianPhone, needPhone: false);
        if (clean is null)
        {
            return Problems.Reject(error!);
        }

        var student = await db.Students.SingleAsync(s => s.Id == id, ct);
        student.FirstName = clean.FirstName;
        student.LastName = clean.LastName;
        student.School = clean.School;
        student.Grade = clean.Grade;
        student.GuardianName = clean.GuardianName;
        var guardianChanged = student.GuardianPhone != clean.GuardianPhone;
        student.GuardianPhone = clean.GuardianPhone;
        await db.SaveChangesAsync(ct);

        // Parent links were made for the old number, with every teacher, so a new guardian starts with none and must be invited again.
        if (guardianChanged)
        {
            await ParentEndpoints.DropLinksAsync(db, accounts, id, allTeachers: true, ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Enrol(
        Guid id, EnrolRequest request, StudentsDbContext db, IClassDirectory classes, IPlanGate plans, TimeProvider clock, CancellationToken ct)
    {
        // The student must already be one of this teacher's students.
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        var (cls, classProblem) = await FindOpenClass(request.ClassId, classes, ct);
        if (cls is null)
        {
            return classProblem!;
        }

        var plan = StudentRules.ParseFeePlan(request.FeePlan);
        if (plan is null)
        {
            return Problems.Reject("fee_plan_invalid");
        }

        var (discount, discountError) = StudentRules.CheckDiscount(plan.Value, request.DiscountAmount, cls.MonthlyFee);
        if (discount is null)
        {
            return Problems.Reject(discountError!);
        }

        if (await ClassIsFull(db, plans, cls, ct))
        {
            return Problems.Reject("plan_limit_students", StatusCodes.Status403Forbidden);
        }

        var student = await db.Students.SingleAsync(s => s.Id == id, ct);
        var (enrolment, problem) = await EnrolAsync(db, student, false, cls, request.IndexNumber, plan.Value, discount.Value, EnrolmentStatus.Active, clock, ct);
        if (enrolment is null)
        {
            return problem!;
        }

        return await SaveAsync(db, ct) ?? Results.Ok(ToRow(enrolment, TitlesOf(cls)));
    }

    private static async Task<IResult> ResetPassword(Guid id, StudentsDbContext db, IAccountProvisioner accounts, CancellationToken ct)
    {
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        var userId = await db.Students.Where(s => s.Id == id).Select(s => s.UserId).SingleAsync(ct);
        var otp = userId is null ? null : await accounts.ResetStudentPasswordAsync(userId.Value, ct);
        return otp is null ? Problems.Reject("account_not_resettable", StatusCodes.Status409Conflict) : Results.Ok(new OneTimePasswordDto(otp));
    }

    private static async Task<IResult> UpdateEnrolment(
        Guid id, UpdateEnrolmentRequest request, StudentsDbContext db, IClassDirectory classes, IPlanGate plans, TimeProvider clock, CancellationToken ct)
    {
        var row = await db.Enrolments.Include(e => e.Student).SingleOrDefaultAsync(e => e.Id == id, ct);
        if (row is null)
        {
            return Results.NotFound();
        }

        var cls = await classes.FindAsync(row.ClassId, ct);
        if (cls is null)
        {
            return Results.NotFound();
        }

        var plan = StudentRules.ParseFeePlan(request.FeePlan ?? row.FeePlan.ToString());
        if (plan is null)
        {
            return Problems.Reject("fee_plan_invalid");
        }

        var (discount, discountError) = StudentRules.CheckDiscount(plan.Value, request.DiscountAmount ?? row.DiscountAmount, cls.MonthlyFee);
        if (discount is null)
        {
            return Problems.Reject(discountError!);
        }

        var status = row.Status;
        if (request.Status is not null)
        {
            if (!Enum.TryParse(request.Status, ignoreCase: true, out status) || !Enum.IsDefined(status) || status == EnrolmentStatus.Pending && row.Status != EnrolmentStatus.Pending)
            {
                return Problems.Reject("status_invalid");
            }
        }

        if (request.IndexNumber is { } index)
        {
            if (index is < 1 or > 99999)
            {
                return Problems.Reject("index_invalid");
            }

            if (index != row.IndexNumber && await db.Enrolments.AnyAsync(e => e.ClassId == row.ClassId && e.IndexNumber == index, ct))
            {
                return Problems.Reject("index_taken", StatusCodes.Status409Conflict);
            }

            row.IndexNumber = index;
        }

        if (row.Status != EnrolmentStatus.Active && status == EnrolmentStatus.Active && await ClassIsFull(db, plans, cls, ct))
        {
            return Problems.Reject("plan_limit_students", StatusCodes.Status403Forbidden);
        }

        if (status != row.Status)
        {
            row.LeftOn = status == EnrolmentStatus.Left ? Problems.Today(clock) : null;
        }

        row.Status = status;
        row.FeePlan = plan.Value;
        row.DiscountAmount = discount.Value;
        return await SaveAsync(db, ct) ?? Results.Ok(ToRow(row, TitlesOf(cls)));
    }

    private static async Task<IResult> Approve(Guid id, StudentsDbContext db, IClassDirectory classes, IPlanGate plans, TimeProvider clock, CancellationToken ct)
    {
        var row = await db.Enrolments.Include(e => e.Student).SingleOrDefaultAsync(e => e.Id == id, ct);
        if (row is null)
        {
            return Results.NotFound();
        }

        if (row.Status != EnrolmentStatus.Pending)
        {
            return Problems.Reject("not_pending", StatusCodes.Status409Conflict);
        }

        var cls = await classes.FindAsync(row.ClassId, ct);
        if (cls is not null && await ClassIsFull(db, plans, cls, ct))
        {
            return Problems.Reject("plan_limit_students", StatusCodes.Status403Forbidden);
        }

        row.Status = EnrolmentStatus.Active;
        row.JoinedOn = Problems.Today(clock);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToRow(row, await classes.FindManyAsync([row.ClassId], ct)));
    }

    // Turning a request down removes it. The student's record stays, because they may have an account or other classes.
    private static async Task<IResult> Reject(Guid id, StudentsDbContext db, CancellationToken ct)
    {
        var row = await db.Enrolments.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (row is null)
        {
            return Results.NotFound();
        }

        if (row.Status != EnrolmentStatus.Pending)
        {
            return Problems.Reject("not_pending", StatusCodes.Status409Conflict);
        }

        db.Enrolments.Remove(row);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>True when the teacher's plan caps students per class and the class already has that many active students. Pending requests do not count.</summary>
    private static async Task<bool> ClassIsFull(StudentsDbContext db, IPlanGate plans, ClassSummary cls, CancellationToken ct) =>
        (await plans.ForTeacherAsync(cls.TenantId, ct)).LimitOf(PlanLimits.MaxStudentsPerClass) is { } max
        && await db.Enrolments.CountAsync(e => e.ClassId == cls.Id && e.Status == EnrolmentStatus.Active, ct) >= max;

    /// <summary>The class must exist for this teacher and not be archived.</summary>
    private static async Task<(ClassSummary? Class, IResult? Problem)> FindOpenClass(Guid? classId, IClassDirectory classes, CancellationToken ct)
    {
        var cls = classId is null ? null : await classes.FindAsync(classId.Value, ct);
        if (cls is null)
        {
            return (null, Problems.Reject("class_required"));
        }

        return cls.IsArchived ? (null, Problems.Reject("class_archived", StatusCodes.Status409Conflict)) : (cls, null);
    }

    /// <summary>
    /// Adds the student to the class, or brings back one who left. Nothing is saved here. Index numbers count up
    /// from the highest in the class unless the teacher chose one.
    /// </summary>
    internal static async Task<(Enrolment? Enrolment, IResult? Problem)> EnrolAsync(
        StudentsDbContext db, Student student, bool newStudent, ClassSummary cls, int? requestedIndex, FeePlan plan, decimal discount,
        EnrolmentStatus status, TimeProvider clock, CancellationToken ct, bool ignoreTenant = false)
    {
        var inClass = ignoreTenant ? db.Enrolments.IgnoreQueryFilters() : db.Enrolments;
        inClass = inClass.Where(e => e.ClassId == cls.Id);

        var existing = newStudent ? null : await inClass.SingleOrDefaultAsync(e => e.StudentId == student.Id, ct);
        if (existing is not null)
        {
            if (existing.Status != EnrolmentStatus.Left)
            {
                return (null, Problems.Reject("already_enrolled", StatusCodes.Status409Conflict));
            }

            existing.Status = status;
            existing.LeftOn = null;
            existing.FeePlan = plan;
            existing.DiscountAmount = discount;
            existing.JoinedOn = Problems.Today(clock);
            existing.Student = student;
            return (existing, null);
        }

        int index;
        if (requestedIndex is { } wanted)
        {
            if (wanted is < 1 or > 99999)
            {
                return (null, Problems.Reject("index_invalid"));
            }

            if (await inClass.AnyAsync(e => e.IndexNumber == wanted, ct))
            {
                return (null, Problems.Reject("index_taken", StatusCodes.Status409Conflict));
            }

            index = wanted;
        }
        else
        {
            index = (await inClass.MaxAsync(e => (int?)e.IndexNumber, ct) ?? 0) + 1;
        }

        var enrolment = new Enrolment
        {
            TenantId = cls.TenantId, Student = student, ClassId = cls.Id, IndexNumber = index,
            JoinedOn = Problems.Today(clock), Status = status, FeePlan = plan, DiscountAmount = discount,
        };
        db.Enrolments.Add(enrolment);
        return (enrolment, null);
    }

    /// <summary>Saves, turning a clash with another save made at the same moment into a message instead of an error page.</summary>
    internal static async Task<IResult?> SaveAsync(StudentsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            return Problems.Reject("conflict", StatusCodes.Status409Conflict);
        }
    }

    internal static IReadOnlyDictionary<Guid, ClassSummary> TitlesOf(ClassSummary cls) =>
        new Dictionary<Guid, ClassSummary> { [cls.Id] = cls };

    internal static StudentRowDto ToRow(Enrolment e, IReadOnlyDictionary<Guid, ClassSummary> titles) => new(
        e.Id, e.StudentId, e.ClassId, titles.TryGetValue(e.ClassId, out var c) ? c.Title : "", e.IndexNumber,
        e.Student.FirstName, e.Student.LastName, e.Student.FullName, e.Student.Phone, e.Student.School, e.Student.Grade,
        e.Status.ToString(), e.FeePlan.ToString(), e.DiscountAmount, e.JoinedOn);

    private static StudentDto ToStudent(Student s, List<Enrolment> enrolments, IReadOnlyDictionary<Guid, ClassSummary> titles, bool hasParent) => new(
        s.Id, s.FirstName, s.LastName, s.FullName, s.Phone, s.School, s.Grade, s.GuardianName, s.GuardianPhone, s.UserId is not null, hasParent,
        [.. enrolments.OrderBy(e => e.JoinedOn).Select(e => new EnrolmentDto(
            e.Id, e.ClassId, titles.TryGetValue(e.ClassId, out var c) ? c.Title : "", e.IndexNumber,
            e.Status.ToString(), e.FeePlan.ToString(), e.DiscountAmount, e.JoinedOn))]);
}
