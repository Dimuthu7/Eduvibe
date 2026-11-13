using GuruBook.Modules.Support.Domain;
using GuruBook.Modules.Support.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Support.Services;

/// <summary>A ticket without its screenshot, which is fetched on its own so a list never carries pictures.</summary>
internal sealed record TicketRow(
    Guid Id, Guid TenantId, string Kind, string Details, string? Screen, TicketStatus Status, bool HasShot,
    DateTimeOffset At, DateTimeOffset? AnsweredAt);

/// <summary>
/// Reads tickets with their conversation. The teacher's own screen and the Super Admin's inbox show the same rows, so they are built the
/// same way; the tenant filter is what makes a teacher see only their own.
/// </summary>
internal static class TicketReader
{
    /// <summary>The newest tickets first, with every reply in the order it was written.</summary>
    public static async Task<(List<TicketRow> Tickets, ILookup<Guid, TicketReply> Replies)> ReadAsync(
        SupportDbContext db, IQueryable<Ticket> query, CancellationToken ct)
    {
        // Ids are time ordered, so they sort the same as the times and work on every database the tests use.
        var tickets = await query
            .OrderByDescending(t => t.Id)
            .Select(t => new TicketRow(
                t.Id, t.TenantId, t.Kind, t.Details, t.Screen, t.Status, t.ShotContentType != null, t.CreatedAt, t.AnsweredAt))
            .ToListAsync(ct);
        var ids = tickets.Select(t => t.Id).ToList();
        var replies = await db.Replies.Where(r => ids.Contains(r.TicketId)).OrderBy(r => r.Id).ToListAsync(ct);
        return (tickets, replies.ToLookup(r => r.TicketId));
    }

    public static TicketReplyDto[] RepliesOf(ILookup<Guid, TicketReply> replies, Guid ticketId) =>
        [.. replies[ticketId].Select(r => new TicketReplyDto(r.Id, r.Body, r.FromSupport, r.CreatedAt))];

    public static TicketDto ToDto(TicketRow t, ILookup<Guid, TicketReply> replies) => new(
        t.Id, t.Kind, t.Details, t.Screen, t.Status.ToString(), t.HasShot, t.At, t.AnsweredAt, RepliesOf(replies, t.Id));

    public static AdminTicketDto ToAdminDto(TicketRow t, ILookup<Guid, TicketReply> replies, string teacherName) => new(
        t.Id, t.TenantId, teacherName, t.Kind, t.Details, t.Screen, t.Status.ToString(), t.HasShot,
        t.At, t.AnsweredAt, RepliesOf(replies, t.Id));
}
