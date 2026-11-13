using GuruBook.Modules.Support.Domain;
using GuruBook.Modules.Support.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Phone;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Support.Endpoints;

/// <summary>
/// The Super Admin's ticket inbox: every teacher's tickets, the reply that goes back, and where each one has got to. The teacher is told
/// in the app when support writes, so they do not have to keep the screen open.
/// </summary>
internal static class AdminEndpoints
{
    private const int MaxBody = 2000;
    private const int MaxHours = 100;

    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin").RequireAuthorization(Policies.SuperAdmin);
        admin.MapGet("/tickets", All);
        admin.MapPost("/tickets/{id:guid}/replies", Reply);
        admin.MapPut("/tickets/{id:guid}/status", SetStatus);
        admin.MapGet("/tickets/{id:guid}/shot", Shot);
        admin.MapGet("/settings", GetSettings);
        admin.MapPut("/settings", SaveSettings);
    }

    // Every teacher's tickets, newest first; status=Open, InProgress or Resolved narrows it to one.
    private static async Task<IResult> All(string? status, SupportDbContext db, ITeacherDirectory teachers, CancellationToken ct)
    {
        var query = db.Tickets.AsQueryable();
        if (Problems.Clean(status) is { } wanted)
        {
            if (!Enum.TryParse<TicketStatus>(wanted, ignoreCase: true, out var only))
            {
                return Problems.Reject("ticket_status_invalid");
            }

            query = query.Where(t => t.Status == only);
        }

        return Results.Ok(await ListAsync(db, query, teachers, ct));
    }

    private static async Task<IResult> Reply(
        Guid id, NewReplyRequest request, SupportDbContext db, INotifier notifier, TimeProvider clock, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return Results.NotFound();
        }

        if (Problems.Clean(request.Body) is not { } body || body.Length > MaxBody)
        {
            return Problems.Reject("ticket_details_required");
        }

        // The Super Admin belongs to no teacher, so the row is stamped with the teacher whose ticket it is.
        var reply = new TicketReply { TenantId = ticket.TenantId, TicketId = id, Body = body, FromSupport = true };
        db.Replies.Add(reply);
        ticket.AnsweredAt = clock.GetUtcNow();
        if (ticket.Status == TicketStatus.Open)
        {
            ticket.Status = TicketStatus.InProgress;
        }

        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(
            [new NotifyRequest(ticket.TenantId, "support_reply", new { kind = ticket.Kind }, Link: "/help", OnceKey: $"support:{reply.Id}")],
            ct);
        return Results.Ok(await OneAsync(db, id, ct));
    }

    private static async Task<IResult> SetStatus(Guid id, StatusRequest request, SupportDbContext db, CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return Results.NotFound();
        }

        if (!Enum.TryParse<TicketStatus>(Problems.Clean(request.Status), ignoreCase: true, out var status))
        {
            return Problems.Reject("ticket_status_invalid");
        }

        ticket.Status = status;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await OneAsync(db, id, ct));
    }

    private static async Task<IResult> Shot(Guid id, SupportDbContext db, HttpContext http, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return ticket?.Shot is { Length: > 0 } shot && ticket.ShotContentType is { } type
            ? TicketEndpoints.Send(shot, type, ticket.UpdatedAt, http)
            : Results.NoContent();
    }

    private static async Task<IResult> GetSettings(SupportDbContext db, CancellationToken ct)
    {
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        return Results.Ok(new SupportSettingsDto(settings?.Phone, settings?.Hours));
    }

    private static async Task<IResult> SaveSettings(SupportSettingsDto request, SupportDbContext db, CancellationToken ct)
    {
        var phone = Problems.Clean(request.Phone);
        if (phone is not null)
        {
            if (PhoneNumber.Normalize(phone) is not { } normalized)
            {
                return Problems.Reject("phone_invalid");
            }

            phone = normalized;
        }

        var hours = Problems.Clean(request.Hours);
        if (hours is { Length: > MaxHours })
        {
            return Problems.Reject("support_hours_too_long");
        }

        var settings = await db.Settings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new SupportSettings();
            db.Settings.Add(settings);
        }

        settings.Phone = phone;
        settings.Hours = hours;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new SupportSettingsDto(settings.Phone, settings.Hours));
    }

    private static async Task<AdminTicketDto> OneAsync(SupportDbContext db, Guid id, CancellationToken ct)
    {
        var tickets = await ListAsync(db, db.Tickets.Where(t => t.Id == id), null, ct);
        return tickets[0];
    }

    private static async Task<List<AdminTicketDto>> ListAsync(
        SupportDbContext db, IQueryable<Ticket> query, ITeacherDirectory? teachers, CancellationToken ct)
    {
        var (tickets, replies) = await TicketReader.ReadAsync(db, query, ct);
        var names = new Dictionary<Guid, string>();
        if (teachers is not null)
        {
            foreach (var teacherId in tickets.Select(t => t.TenantId).Distinct())
            {
                names[teacherId] = await teachers.FindNameAsync(teacherId, ct) ?? "";
            }
        }

        return [.. tickets.Select(t => TicketReader.ToAdminDto(t, replies, names.GetValueOrDefault(t.TenantId, "")))];
    }
}
