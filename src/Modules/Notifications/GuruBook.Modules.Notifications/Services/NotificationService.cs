using System.Text.Json;
using GuruBook.Modules.Notifications.Domain;
using GuruBook.Modules.Notifications.Push;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Notifications.Services;

/// <summary>
/// Stores notifications. Asking for the same once-key again does nothing; a group-key joins the unread notification that already has it.
/// Whatever goes wrong here is logged and dropped, because telling someone must never undo what they were told about.
/// </summary>
internal sealed class NotificationService(NotificationsDbContext db, PushDelivery push, ILogger<NotificationService> log) : INotifier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task NotifyAsync(IReadOnlyCollection<NotifyRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return;
        }

        IReadOnlyCollection<Notification> added;
        try
        {
            added = await StoreAsync(requests, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A once-key that two requests added at the same moment, or the database being unavailable: nothing else depends on this.
            log.LogWarning(e, "Could not store {Count} notification(s)", requests.Count);
            db.ChangeTracker.Clear();
            return;
        }

        // Only new lines go to phones: a repeat of something already sent (same once-key) or one more event joining an unread line does not buzz again.
        await push.DeliverAsync(added, cancellationToken);
    }

    public async Task ForgetAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        await db.Notifications.Where(n => n.RecipientId == recipientId).ExecuteDeleteAsync(cancellationToken);
        await db.PushDevices.Where(d => d.UserId == recipientId || d.TenantId == recipientId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<List<Notification>> StoreAsync(IReadOnlyCollection<NotifyRequest> requests, CancellationToken ct)
    {
        var onceKeys = requests.Where(r => r.OnceKey is not null).Select(r => r.OnceKey!).Distinct().ToList();
        var users = requests.Select(r => r.RecipientId).Distinct().ToList();
        var seen = onceKeys.Count == 0
            ? []
            : (await db.Notifications.Where(n => users.Contains(n.RecipientId) && n.OnceKey != null && onceKeys.Contains(n.OnceKey))
                .Select(n => new { n.RecipientId, n.OnceKey })
                .ToListAsync(ct)).Select(n => (n.RecipientId, n.OnceKey!)).ToHashSet();

        var groupKeys = requests.Where(r => r.GroupKey is not null).Select(r => r.GroupKey!).Distinct().ToList();
        var open = groupKeys.Count == 0
            ? []
            : await db.Notifications.Where(n => users.Contains(n.RecipientId) && n.ReadAt == null && n.GroupKey != null && groupKeys.Contains(n.GroupKey)).ToListAsync(ct);

        var created = new List<Notification>();
        foreach (var request in requests)
        {
            if (request.OnceKey is { } once)
            {
                if (!seen.Add((request.RecipientId, once)))
                {
                    continue;
                }
            }
            else if (request.GroupKey is { } group && open.FirstOrDefault(n => n.RecipientId == request.RecipientId && n.GroupKey == group) is { } existing)
            {
                existing.Count++;
                existing.ParamsJson = Serialize(request.Params);
                existing.Link = request.GroupedLink ?? request.Link;
                continue;
            }

            var added = new Notification
            {
                RecipientId = request.RecipientId, Kind = request.Kind, ParamsJson = Serialize(request.Params), Link = request.Link,
                OnceKey = request.OnceKey, GroupKey = request.GroupKey,
            };
            db.Notifications.Add(added);
            created.Add(added);
            if (request.GroupKey is not null)
            {
                open.Add(added);
            }
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    private static string Serialize(object? value) => value is null ? "{}" : JsonSerializer.Serialize(value, Json);
}
