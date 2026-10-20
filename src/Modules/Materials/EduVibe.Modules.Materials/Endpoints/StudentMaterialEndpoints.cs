using EduVibe.Modules.Materials.Domain;
using EduVibe.Modules.Materials.Drive;
using EduVibe.Shared.Modules;
using EduVibe.Shared.Plans;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Materials.Endpoints;

/// <summary>A material as its student sees it, with the class and teacher it comes from and the names of the folders it sits in, outermost first.</summary>
public sealed record StudentMaterialDto(MaterialDto Material, string ClassTitle, string? TeacherName, IReadOnlyList<string> FolderPath);

/// <summary>
/// What a signed-in student can open: materials of the classes they are active in (and that are not archived), and only while that teacher's plan includes Drive materials.
/// The bytes come through the API from the teacher's Drive, so nothing in Drive is shared and leaving a class ends access at once.
/// </summary>
internal static class StudentMaterialEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var mine = group.MapGroup("/mine").RequireAuthorization(Policies.Student);
        mine.MapGet("", List);
        mine.MapGet("/{id:guid}/file", Open);
    }

    private static async Task<IResult> List(
        MaterialsDbContext db, IClassRoster roster, IClassDirectory classes, ITeacherDirectory teachers, IPlanGate plans, ITenantContext user, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var enrolments = await roster.ActiveEnrolmentsOfUserAsync(userId, ct);
        var classIds = enrolments.Select(e => e.ClassId).Distinct().ToList();
        if (classIds.Count == 0)
        {
            return Results.Ok(Array.Empty<StudentMaterialDto>());
        }

        // The student is not a teacher, so the teacher filter is lifted and the student's own classes are the only way in.
        var materials = await db.Materials.IgnoreQueryFilters().AsNoTracking().Where(m => classIds.Contains(m.ClassId)).ToListAsync(ct);
        var allowed = new Dictionary<Guid, bool>();
        var names = new Dictionary<Guid, string?>();
        foreach (var tenantId in materials.Select(m => m.TenantId).Distinct())
        {
            allowed[tenantId] = (await plans.ForTeacherAsync(tenantId, ct)).Has(PlanFeatures.DriveMaterials);
            names[tenantId] = await teachers.FindNameAsync(tenantId, ct);
        }

        var folders = await db.Folders.IgnoreQueryFilters().AsNoTracking().Where(f => classIds.Contains(f.ClassId)).ToDictionaryAsync(f => f.Id, ct);
        var titles = await classes.FindManyInAnyTenantAsync(classIds, ct);
        var items = materials
            .Where(m => allowed[m.TenantId] && titles.TryGetValue(m.ClassId, out var cls) && !cls.IsArchived)
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.CreatedAt)
            .Select(m => new StudentMaterialDto(MaterialFiles.ToDto(m), titles.TryGetValue(m.ClassId, out var cls) ? cls.Title : string.Empty, names[m.TenantId], PathOf(m.FolderId, folders)))
            .ToArray();
        return Results.Ok(items);
    }

    private static List<string> PathOf(Guid? folderId, Dictionary<Guid, MaterialFolder> folders)
    {
        var path = new List<string>();
        for (var id = folderId; id is { } current && folders.TryGetValue(current, out var folder) && !folder.IsKindRoot && path.Count < FolderEndpoints.MaxDepth; id = folder.ParentId)
        {
            path.Insert(0, folder.Name);
        }

        return path;
    }

    private static async Task<IResult> Open(
        Guid id, bool? download, MaterialsDbContext db, IClassRoster roster, IClassDirectory classes, IPlanGate plans, IDriveClient drive, DriveTokens tokens,
        ITenantContext user, HttpResponse response, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var material = await db.Materials.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
        var enrolments = await roster.ActiveEnrolmentsOfUserAsync(userId, ct);
        if (material is null || !enrolments.Any(e => e.ClassId == material.ClassId))
        {
            return Results.NotFound();
        }

        // A class the teacher archived is closed to its students, files included, just like the rest of the class.
        if (!(await plans.ForTeacherAsync(material.TenantId, ct)).Has(PlanFeatures.DriveMaterials)
            || await classes.FindInAnyTenantAsync(material.ClassId, ct) is not { IsArchived: false })
        {
            return Results.NotFound();
        }

        var connection = await db.Connections.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(c => c.TenantId == material.TenantId, ct);
        return await MaterialFiles.StreamAsync(material, connection, tokens, drive, db, response, download ?? false, ct);
    }
}
