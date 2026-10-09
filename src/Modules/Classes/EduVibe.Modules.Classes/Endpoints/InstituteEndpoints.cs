using EduVibe.Modules.Classes.Domain;
using EduVibe.Shared.Geography;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Classes.Endpoints;

public sealed record InstituteRequest(string? Name, string? District, string? Town, string? Address, string? Phone, bool? IsActive);
public sealed record InstituteDto(Guid Id, string Name, string? District, string? Town, string? Address, string? Phone, bool IsActive, Guid[] TeacherIds);
public sealed record SetTeachersRequest(Guid[]? TeacherIds);

internal static class InstituteEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/institutes").RequireAuthorization(Policies.SuperAdmin);
        admin.MapGet("", List);
        admin.MapPost("", Create);
        admin.MapPut("/{id:guid}", Update);
        admin.MapPut("/{id:guid}/teachers", SetTeachers);

        group.MapGet("/my-institutes", Mine).RequireAuthorization(Policies.Teacher);
    }

    private static async Task<IResult> List(ClassesDbContext db, CancellationToken ct)
    {
        var institutes = await db.Institutes.OrderBy(i => i.Name).ToListAsync(ct);
        var links = await db.InstituteTeachers.ToListAsync(ct);
        return Results.Ok(institutes.Select(i => ToDto(i, links)).ToList());
    }

    private static async Task<IResult> Create(InstituteRequest request, ClassesDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        var institute = new Institute { Name = request.Name!.Trim() };
        Apply(institute, request);
        db.Institutes.Add(institute);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/classes/institutes/{institute.Id}", ToDto(institute, []));
    }

    private static async Task<IResult> Update(Guid id, InstituteRequest request, ClassesDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        var institute = await db.Institutes.FindAsync([id], ct);
        if (institute is null)
        {
            return Results.NotFound();
        }

        institute.Name = request.Name!.Trim();
        Apply(institute, request);
        await db.SaveChangesAsync(ct);
        var links = await db.InstituteTeachers.Where(l => l.InstituteId == id).ToListAsync(ct);
        return Results.Ok(ToDto(institute, links));
    }

    private static async Task<IResult> SetTeachers(
        Guid id, SetTeachersRequest request, ClassesDbContext db, ITeacherDirectory teachers, CancellationToken ct)
    {
        if (!await db.Institutes.AnyAsync(i => i.Id == id, ct))
        {
            return Results.NotFound();
        }

        var wanted = (request.TeacherIds ?? []).Distinct().ToList();
        var known = await teachers.FindActiveAsync(wanted, ct);
        if (wanted.Any(t => !known.Contains(t)))
        {
            return Results.Problem(statusCode: 400, title: "unknown_teacher", extensions: new Dictionary<string, object?> { ["code"] = "unknown_teacher" });
        }

        var current = await db.InstituteTeachers.Where(l => l.InstituteId == id).ToListAsync(ct);
        db.InstituteTeachers.RemoveRange(current.Where(l => !wanted.Contains(l.TeacherId)));
        db.InstituteTeachers.AddRange(wanted
            .Where(t => current.All(l => l.TeacherId != t))
            .Select(t => new InstituteTeacher { InstituteId = id, TeacherId = t }));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Mine(ITenantContext tenant, ClassesDbContext db, CancellationToken ct)
    {
        var teacherId = tenant.TenantId!.Value;
        var rows = await db.Institutes
            .Where(i => i.IsActive && db.InstituteTeachers.Any(l => l.InstituteId == i.Id && l.TeacherId == teacherId))
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(i => ToDto(i, [])).ToList());
    }

    private static IResult? Validate(InstituteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            return Problem("name_required");
        }

        return SriLankaDistricts.Canonical(request.District) is null ? Problem("district_invalid") : null;
    }

    private static IResult Problem(string code) =>
        Results.Problem(statusCode: 400, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static void Apply(Institute institute, InstituteRequest request)
    {
        institute.District = SriLankaDistricts.Canonical(request.District);
        institute.Town = Clean(request.Town);
        institute.Address = Clean(request.Address);
        institute.Phone = Clean(request.Phone);
        institute.IsActive = request.IsActive ?? institute.IsActive;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InstituteDto ToDto(Institute i, IEnumerable<InstituteTeacher> links) =>
        new(i.Id, i.Name, i.District, i.Town, i.Address, i.Phone, i.IsActive,
            [.. links.Where(l => l.InstituteId == i.Id).Select(l => l.TeacherId)]);
}
