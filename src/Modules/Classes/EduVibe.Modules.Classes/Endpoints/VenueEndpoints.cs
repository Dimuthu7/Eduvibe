using EduVibe.Modules.Classes.Domain;
using EduVibe.Shared.Geography;
using EduVibe.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Classes.Endpoints;

public sealed record VenueRequest(string? Name, string? District, string? Town, string? Address, bool? IsActive);
public sealed record VenueDto(Guid Id, string Name, string? District, string? Town, string? Address, bool IsActive);

/// <summary>A teacher's own private venues (home class, hired hall). The tenant filter keeps each teacher to their own.</summary>
internal static class VenueEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var venues = group.MapGroup("/venues").RequireAuthorization(Policies.Teacher);
        venues.MapGet("", List);
        venues.MapPost("", Create);
        venues.MapPut("/{id:guid}", Update);
    }

    private static async Task<IResult> List(ClassesDbContext db, CancellationToken ct) =>
        Results.Ok((await db.Venues.OrderBy(v => v.Name).ToListAsync(ct)).Select(ToDto).ToList());

    private static async Task<IResult> Create(VenueRequest request, ClassesDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        var venue = new Venue { Name = request.Name!.Trim() };
        Apply(venue, request);
        db.Venues.Add(venue);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/classes/venues/{venue.Id}", ToDto(venue));
    }

    private static async Task<IResult> Update(Guid id, VenueRequest request, ClassesDbContext db, CancellationToken ct)
    {
        if (Validate(request) is { } problem)
        {
            return problem;
        }

        var venue = await db.Venues.FindAsync([id], ct);
        if (venue is null)
        {
            return Results.NotFound();
        }

        venue.Name = request.Name!.Trim();
        Apply(venue, request);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(venue));
    }

    private static IResult? Validate(VenueRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            return Problems.Reject("name_required");
        }

        return SriLankaDistricts.Canonical(request.District) is null ? Problems.Reject("district_invalid") : null;
    }

    private static void Apply(Venue venue, VenueRequest request)
    {
        venue.District = SriLankaDistricts.Canonical(request.District);
        venue.Town = Problems.Clean(request.Town);
        venue.Address = Problems.Clean(request.Address);
        venue.IsActive = request.IsActive ?? venue.IsActive;
    }

    private static VenueDto ToDto(Venue v) => new(v.Id, v.Name, v.District, v.Town, v.Address, v.IsActive);
}
