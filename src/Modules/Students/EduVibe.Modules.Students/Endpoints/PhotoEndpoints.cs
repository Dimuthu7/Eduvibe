using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Security;
using EduVibe.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace EduVibe.Modules.Students.Endpoints;

/// <summary>Which students have a photo, and the version to ask for it by. EnrolmentIds are the teacher's own, so rows that only know an enrolment can find the photo.</summary>
public sealed record StudentPhotoDto(Guid StudentId, long Version, Guid[] EnrolmentIds);

/// <summary>Profile photos. A teacher sets, replaces and removes the photo of a student they teach; the student sees their own.</summary>
internal static class PhotoEndpoints
{
    /// <summary>The app sends a 320 px square JPEG of 15 to 40 kB; this leaves room for a phone's own PNG or WebP without letting the table grow wild.</summary>
    internal const int MaxBytes = 256 * 1024;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/me/photo", MyPhoto).RequireAuthorization(Policies.Student);

        var teacher = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        teacher.MapGet("/photos", List);
        teacher.MapGet("/{id:guid}/photo", Get);
        teacher.MapPut("/{id:guid}/photo", Put);
        teacher.MapDelete("/{id:guid}/photo", Delete);
    }

    // One call tells the app which of the teacher's students have a photo, so it asks for images only where there is one.
    private static async Task<IResult> List(StudentsDbContext db, CancellationToken ct)
    {
        // The tenant filter on enrolments is what limits this to the teacher's own students.
        var enrolments = await db.Enrolments
            .Where(e => db.StudentPhotos.Any(p => p.StudentId == e.StudentId))
            .Select(e => new { e.StudentId, e.Id })
            .ToListAsync(ct);
        var ids = enrolments.Select(e => e.StudentId).Distinct().ToList();
        var photos = await db.StudentPhotos.Where(p => ids.Contains(p.StudentId)).Select(p => new { p.StudentId, p.UpdatedAt }).ToListAsync(ct);
        return Results.Ok(photos.Select(p => new StudentPhotoDto(
            p.StudentId, p.UpdatedAt.ToUnixTimeMilliseconds(), [.. enrolments.Where(e => e.StudentId == p.StudentId).Select(e => e.Id)])).ToList());
    }

    private static async Task<IResult> Get(Guid id, StudentsDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        var photo = await db.StudentPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.StudentId == id, ct);
        if (photo is null)
        {
            return Results.NotFound();
        }

        // The app asks with the version in the address, so a changed photo is a new address and an unchanged one is kept on the phone.
        http.Response.Headers.CacheControl = "private, max-age=604800";
        return Send(photo, http);
    }

    // 204 rather than 404 when there is none, so a student without a photo does not fill the browser console with errors.
    private static async Task<IResult> MyPhoto(ITenantContext user, StudentsDbContext db, HttpContext http, CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var photo = await db.StudentPhotos.AsNoTracking()
            .Where(p => db.Students.Any(s => s.Id == p.StudentId && s.UserId == userId))
            .SingleOrDefaultAsync(ct);
        if (photo is null)
        {
            return Results.NoContent();
        }

        http.Response.Headers.CacheControl = "private, no-cache";
        return Send(photo, http);
    }

    private static async Task<IResult> Put(Guid id, HttpRequest request, StudentsDbContext db, CancellationToken ct)
    {
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        if (request.ContentLength > MaxBytes)
        {
            return Problems.Reject("photo_too_large", StatusCodes.Status413PayloadTooLarge);
        }

        var bytes = await ReadLimitedAsync(request.Body, ct);
        if (bytes is null)
        {
            return Problems.Reject("photo_too_large", StatusCodes.Status413PayloadTooLarge);
        }

        if (ImageType(bytes) is not { } type)
        {
            return Problems.Reject("photo_invalid");
        }

        var photo = await db.StudentPhotos.SingleOrDefaultAsync(p => p.StudentId == id, ct);
        if (photo is null)
        {
            photo = new StudentPhoto { StudentId = id, ContentType = type, Data = bytes };
            db.StudentPhotos.Add(photo);
        }
        else
        {
            photo.ContentType = type;
            photo.Data = bytes;
        }

        await db.SaveChangesAsync(ct);
        var enrolments = await db.Enrolments.Where(e => e.StudentId == id).Select(e => e.Id).ToListAsync(ct);
        return Results.Ok(new StudentPhotoDto(id, photo.UpdatedAt.ToUnixTimeMilliseconds(), [.. enrolments]));
    }

    private static async Task<IResult> Delete(Guid id, StudentsDbContext db, CancellationToken ct)
    {
        if (!await db.Enrolments.AnyAsync(e => e.StudentId == id, ct))
        {
            return Results.NotFound();
        }

        await db.StudentPhotos.Where(p => p.StudentId == id).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static IResult Send(StudentPhoto photo, HttpContext http)
    {
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(
            photo.Data, photo.ContentType, lastModified: photo.UpdatedAt,
            entityTag: new EntityTagHeaderValue($"\"{photo.UpdatedAt.ToUnixTimeMilliseconds()}\""));
    }

    /// <summary>The whole body, or null when it is longer than <see cref="MaxBytes"/>.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>The image type from the first bytes (JPEG, PNG or WebP), or null for anything else.</summary>
    internal static string? ImageType(ReadOnlySpan<byte> b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (b.Length > 8 && b[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (b.Length > 12 && b[..4].SequenceEqual<byte>("RIFF"u8) && b[8..12].SequenceEqual<byte>("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
