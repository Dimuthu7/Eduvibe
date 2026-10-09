using EduVibe.Modules.Catalog.Domain;
using EduVibe.Shared.Domain;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Catalog.Endpoints;

public sealed record CatalogItemDto(Guid Id, string Name, int SortOrder, bool IsActive);
public sealed record CatalogItemRequest(string? Name, bool? IsActive);

/// <summary>Streams and subjects: everyone signed in can read them, only the Super Admin changes them.</summary>
internal static class CatalogEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        MapKind<EducationStream>(group, "streams", db => db.Streams);
        MapKind<Subject>(group, "subjects", db => db.Subjects);
    }

    private static void MapKind<T>(RouteGroupBuilder group, string route, Func<CatalogDbContext, DbSet<T>> set)
        where T : Entity, ICatalogItem
    {
        var items = group.MapGroup($"/{route}");
        items.MapGet("", async (CatalogDbContext db, bool? includeInactive, CancellationToken ct) =>
        {
            var query = set(db).AsQueryable();
            if (includeInactive != true)
            {
                query = query.Where(i => i.IsActive);
            }

            var rows = await query.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct);
            return Results.Ok(rows.Select(ToDto).ToList());
        });

        items.MapPost("", async (CatalogItemRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            var name = Clean(request.Name);
            if (name is null)
            {
                return Problem(StatusCodes.Status400BadRequest, "name_required");
            }

            if (await set(db).AnyAsync(i => i.Name.ToLower() == name.ToLower(), ct))
            {
                return Problem(StatusCodes.Status409Conflict, "name_taken");
            }

            var next = ((await set(db).MaxAsync(i => (int?)i.SortOrder, ct)) ?? 0) + 10;
            var item = Create<T>(name, next);
            set(db).Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/catalog/{route}/{item.Id}", ToDto(item));
        }).RequireAuthorization(Policies.SuperAdmin);

        items.MapPut("/{id:guid}", async (Guid id, CatalogItemRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            var item = await set(db).FindAsync([id], ct);
            if (item is null)
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                var name = Clean(request.Name);
                if (name is null)
                {
                    return Problem(StatusCodes.Status400BadRequest, "name_required");
                }

                if (await set(db).AnyAsync(i => i.Id != id && i.Name.ToLower() == name.ToLower(), ct))
                {
                    return Problem(StatusCodes.Status409Conflict, "name_taken");
                }

                item.Name = name;
            }

            item.IsActive = request.IsActive ?? item.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(item));
        }).RequireAuthorization(Policies.SuperAdmin);
    }

    private static string? Clean(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 80 ? null : trimmed;
    }

    private static CatalogItemDto ToDto(ICatalogItem item) => new(((Entity)item).Id, item.Name, item.SortOrder, item.IsActive);

    private static T Create<T>(string name, int sortOrder) where T : ICatalogItem =>
        typeof(T) == typeof(EducationStream)
            ? (T)(object)new EducationStream { Name = name, SortOrder = sortOrder }
            : (T)(object)new Subject { Name = name, SortOrder = sortOrder };

    private static IResult Problem(int status, string code) =>
        Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
