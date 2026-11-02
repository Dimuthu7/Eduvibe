using System.Collections.Concurrent;
using System.Text.Json;
using GuruBook.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Notifications.Push;

/// <summary>
/// Sends new notifications to the phones of the people they are for. What goes over the wire is the notification's kind and words
/// (not finished text), and the phone writes it in the person's language. A device the push service no longer knows is forgotten.
/// A problem here is logged and dropped: the notification is already in the app's list.
/// </summary>
internal sealed class PushDelivery(NotificationsDbContext db, IPushSender sender, ILogger<PushDelivery> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task DeliverAsync(IReadOnlyCollection<Notification> notifications, CancellationToken ct)
    {
        if (!sender.Enabled || notifications.Count == 0)
        {
            return;
        }

        try
        {
            var recipients = notifications.Select(n => n.RecipientId).Distinct().ToList();
            var devices = await db.PushDevices.AsNoTracking()
                .Where(d => recipients.Contains(d.UserId) || (d.TenantId != null && recipients.Contains(d.TenantId.Value)))
                .ToListAsync(ct);
            if (devices.Count == 0)
            {
                return;
            }

            var sends = notifications
                .SelectMany(n => devices.Where(d => d.UserId == n.RecipientId || d.TenantId == n.RecipientId).Select(d => (Notification: n, Device: d)))
                .ToList();
            var gone = new ConcurrentBag<string>();
            await Parallel.ForEachAsync(sends, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (send, token) =>
            {
                var outcome = await sender.SendAsync(new PushTarget(send.Device.Endpoint, send.Device.P256dh, send.Device.Auth), Payload(send.Notification, send.Device), token);
                if (outcome == PushOutcome.Gone)
                {
                    gone.Add(send.Device.Endpoint);
                }
            });

            if (!gone.IsEmpty)
            {
                var endpoints = gone.Distinct().ToList();
                await db.PushDevices.Where(d => endpoints.Contains(d.Endpoint)).ExecuteDeleteAsync(ct);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Could not deliver {Count} push notification(s)", notifications.Count);
        }
    }

    private static string Payload(Notification n, PushDevice device) =>
        JsonSerializer.Serialize(
            new { id = n.Id, kind = n.Kind, @params = JsonDocument.Parse(n.ParamsJson).RootElement, link = n.Link, count = n.Count, lang = device.Language },
            Json);
}
