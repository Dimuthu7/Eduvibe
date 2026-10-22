using System.Text.Json;
using System.Text.Json.Serialization;
using GuruBook.Shared.Auditing;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Privacy;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GuruBook.Modules.Privacy;

public sealed record EraseRequest(string? Confirm);

/// <summary>
/// Export and erase for one teacher or one student. Erasing needs the person's full name typed back, so the wrong record is hard to delete,
/// and a teacher must be switched off first. Erasure runs module by module and can be repeated if it stops part way.
/// </summary>
internal static class PrivacyEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Map(RouteGroupBuilder group)
    {
        // What these return is personal data: nothing between the server and the Super Admin's screen should keep a copy.
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapGet("/teachers/{id:guid}/export", ExportTeacher);
        group.MapPost("/teachers/{id:guid}/erase", EraseTeacher);
        group.MapGet("/students/{id:guid}/export", ExportStudent);
        group.MapPost("/students/{id:guid}/erase", EraseStudent);
    }

    private static async Task<IResult> ExportTeacher(Guid id, ITeacherDirectory teachers, IEnumerable<IPersonalDataSource> sources, CancellationToken ct)
    {
        if (await teachers.FindIdentityAsync(id, ct) is null)
        {
            return Problem("teacher_not_found", StatusCodes.Status404NotFound);
        }

        var export = new SortedDictionary<string, PersonalData>(StringComparer.Ordinal);
        foreach (var source in sources.OrderBy(s => s.Order))
        {
            var data = await source.ExportTeacherAsync(id, ct);
            if (data.Count > 0)
            {
                export[source.Module] = data;
            }
        }

        return File("teacher", id, export);
    }

    private static async Task<IResult> EraseTeacher(
        Guid id, EraseRequest request, ITeacherDirectory teachers, IEnumerable<IPersonalDataSource> sources,
        AuditDbContext audit, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var teacher = await teachers.FindIdentityAsync(id, ct);
        if (teacher is null)
        {
            return Problem("teacher_not_found", StatusCodes.Status404NotFound);
        }

        if (teacher.IsActive)
        {
            return Problem("teacher_still_active", StatusCodes.Status409Conflict);
        }

        if (!Matches(request.Confirm, teacher.FullName))
        {
            return Problem("confirm_mismatch", StatusCodes.Status400BadRequest);
        }

        foreach (var source in sources.OrderBy(s => s.Order))
        {
            await source.EraseTeacherAsync(id, ct);
        }

        await AuditRedaction.RedactTenantAsync(audit, id, ct);
        await Record(audit, tenant, clock, "Teacher", id, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ExportStudent(Guid id, IEnumerable<IPersonalDataSource> sources, CancellationToken ct)
    {
        if (await NameOf(id, sources, ct) is null)
        {
            return Problem("student_not_found", StatusCodes.Status404NotFound);
        }

        var export = new SortedDictionary<string, PersonalData>(StringComparer.Ordinal);
        foreach (var source in sources.OrderBy(s => s.Order))
        {
            var data = await source.ExportStudentAsync(id, ct);
            if (data.Count > 0)
            {
                export[source.Module] = data;
            }
        }

        return File("student", id, export);
    }

    private static async Task<IResult> EraseStudent(
        Guid id, EraseRequest request, IEnumerable<IPersonalDataSource> sources, AuditDbContext audit, ITenantContext tenant, TimeProvider clock, CancellationToken ct)
    {
        var name = await NameOf(id, sources, ct);
        if (name is null)
        {
            return Problem("student_not_found", StatusCodes.Status404NotFound);
        }

        if (!Matches(request.Confirm, name))
        {
            return Problem("confirm_mismatch", StatusCodes.Status400BadRequest);
        }

        foreach (var source in sources.OrderBy(s => s.Order))
        {
            await source.EraseStudentAsync(id, ct);
        }

        await Record(audit, tenant, clock, "Student", id, ct);
        return Results.NoContent();
    }

    private static async Task<string?> NameOf(Guid studentId, IEnumerable<IPersonalDataSource> sources, CancellationToken ct)
    {
        foreach (var source in sources)
        {
            if (await source.FindStudentNameAsync(studentId, ct) is { } name)
            {
                return name;
            }
        }

        return null;
    }

    // Names are compared ignoring case and extra spaces, which is what a person typing from the screen can be expected to get right.
    private static bool Matches(string? typed, string name) =>
        !string.IsNullOrWhiteSpace(typed) && Normalise(typed) == Normalise(name);

    private static string Normalise(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();

    // The trail keeps that an erasure happened, who asked and when, but nothing about the person.
    private static async Task Record(AuditDbContext audit, ITenantContext tenant, TimeProvider clock, string entity, Guid id, CancellationToken ct)
    {
        audit.Entries.Add(new AuditLogEntry
        {
            TenantId = entity == "Teacher" ? id : null,
            UserId = tenant.UserId,
            Entity = entity,
            EntityId = id,
            Action = AuditAction.Deleted,
            After = AuditRedaction.Erased,
            At = clock.GetUtcNow(),
        });
        await audit.SaveChangesAsync(ct);
    }

    private static IResult File(string kind, Guid id, object export)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind, id, exportedAt = DateTimeOffset.UtcNow, data = export }, Json);
        return Results.File(bytes, "application/json", $"{kind}-{id:N}.json");
    }

    private static IResult Problem(string code, int status) =>
        Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
