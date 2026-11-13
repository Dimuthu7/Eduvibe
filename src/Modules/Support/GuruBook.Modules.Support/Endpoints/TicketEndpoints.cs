using GuruBook.Modules.Support.Domain;
using GuruBook.Modules.Support.Services;
using GuruBook.Shared.Images;
using GuruBook.Shared.Plans;
using GuruBook.Shared.Security;
using GuruBook.Shared.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace GuruBook.Modules.Support.Endpoints;

/// <summary>
/// Report an issue, for a teacher on a plan with support. They see the number to call, write what went wrong with an optional screenshot,
/// and read what support writes back. A teacher only ever sees their own tickets, because the tenant filter keeps the rest out.
/// </summary>
internal static class TicketEndpoints
{
    private const int MaxDetails = 2000;
    private const int MaxOpen = 20;

    public static void Map(RouteGroupBuilder group)
    {
        var mine = group.MapGroup("").RequireAuthorization(Policies.Teacher).RequireFeature(PlanFeatures.PrioritySupport);
        mine.MapGet("", Mine);
        mine.MapPost("/tickets", Raise);
        mine.MapPost("/tickets/{id:guid}/replies", Reply);
        mine.MapGet("/tickets/{id:guid}/shot", Shot);
        mine.MapPut("/tickets/{id:guid}/shot", SaveShot);
    }

    /// <summary>The picture, with the header that stops a browser guessing its type.</summary>
    internal static IResult Send(byte[] data, string contentType, DateTimeOffset updatedAt, HttpContext http)
    {
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(
            data, contentType, lastModified: updatedAt,
            entityTag: new EntityTagHeaderValue($"\"{updatedAt.ToUnixTimeMilliseconds()}\""));
    }

    private static async Task<IResult> Mine(SupportDbContext db, CancellationToken ct)
    {
        var (tickets, replies) = await TicketReader.ReadAsync(db, db.Tickets, ct);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return Results.Ok(new SupportDto(settings?.Phone, settings?.Hours, [.. tickets.Select(t => TicketReader.ToDto(t, replies))]));
    }

    private static async Task<IResult> Raise(NewTicketRequest request, SupportDbContext db, ITenantContext user, CancellationToken ct)
    {
        if (user.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        if (!TicketKinds.IsKnown(request.Kind))
        {
            return Problems.Reject("ticket_kind_invalid");
        }

        if (Problems.Clean(request.Details) is not { } details)
        {
            return Problems.Reject("ticket_details_required");
        }

        if (details.Length > MaxDetails)
        {
            return Problems.Reject("ticket_details_too_long");
        }

        // A teacher with a pile of unanswered tickets is not helped by raising more of them.
        if (await db.Tickets.CountAsync(t => t.Status != TicketStatus.Resolved, ct) >= MaxOpen)
        {
            return Problems.Reject("too_many_tickets", StatusCodes.Status409Conflict);
        }

        var ticket = new Ticket
        {
            TenantId = tenantId, Kind = request.Kind!, Details = details, Screen = Problems.Clean(request.Screen),
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await OneAsync(db, ticket.Id, ct));
    }

    private static async Task<IResult> Reply(Guid id, NewReplyRequest request, SupportDbContext db, ITenantContext user, CancellationToken ct)
    {
        if (user.TenantId is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return Results.NotFound();
        }

        if (Problems.Clean(request.Body) is not { } body || body.Length > MaxDetails)
        {
            return Problems.Reject("ticket_details_required");
        }

        db.Replies.Add(new TicketReply { TenantId = tenantId, TicketId = id, Body = body, FromSupport = false });

        // Writing again on a finished ticket opens it, because the teacher is clearly not finished with it.
        if (ticket.Status == TicketStatus.Resolved)
        {
            ticket.Status = TicketStatus.Open;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(await OneAsync(db, id, ct));
    }

    private static async Task<IResult> Shot(Guid id, SupportDbContext db, HttpContext http, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return ticket?.Shot is { Length: > 0 } shot && ticket.ShotContentType is { } type
            ? Send(shot, type, ticket.UpdatedAt, http)
            : Results.NoContent();
    }

    private static async Task<IResult> SaveShot(Guid id, HttpRequest request, SupportDbContext db, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return Results.NotFound();
        }

        if (request.ContentLength > ImageUpload.MaxBytes)
        {
            return Problems.Reject("image_too_large");
        }

        var bytes = await ImageUpload.ReadLimitedAsync(request.Body, ct);
        if (bytes is null)
        {
            return Problems.Reject("image_too_large");
        }

        if (ImageUpload.TypeOf(bytes) is not { } type)
        {
            return Problems.Reject("image_type_invalid");
        }

        ticket.ShotContentType = type;
        ticket.Shot = bytes;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<TicketDto> OneAsync(SupportDbContext db, Guid id, CancellationToken ct)
    {
        var (tickets, replies) = await TicketReader.ReadAsync(db, db.Tickets.Where(t => t.Id == id), ct);
        return TicketReader.ToDto(tickets[0], replies);
    }
}
