using System.Text.Json;
using EduVibe.Modules.WhatsApp.Client;
using EduVibe.Modules.WhatsApp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduVibe.Modules.WhatsApp.Services;

/// <summary>
/// Sends the queued messages. It acts for no teacher (it runs in the background), so it lifts the teacher filter and works from what the log holds.
/// A busy WhatsApp is tried again after 2, 4, 8 minutes; after that, or when WhatsApp refuses the message for good, the message is marked Failed
/// and stops counting against the allowance.
/// </summary>
public sealed class WhatsAppDispatcher(
    WhatsAppDbContext db, IWhatsAppClient client, IOptions<WhatsAppOptions> options, TimeProvider clock, ILogger<WhatsAppDispatcher> log)
{
    public const int MaxAttempts = 4;
    private const int BatchSize = 50;

    /// <summary>Sends what is due, oldest first, and returns how many messages were handled.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // SQLite (used in tests) cannot compare dates in a query, and the queue is short, so due messages are picked after loading.
        var queued = (await db.Messages.IgnoreQueryFilters().Where(m => m.Status == MessageStatus.Queued).OrderBy(m => m.Id).ToListAsync(ct))
            .Where(m => m.NextAttemptAt is null || m.NextAttemptAt <= now)
            .Take(BatchSize)
            .ToList();

        var settings = options.Value;
        foreach (var message in queued)
        {
            var values = JsonSerializer.Deserialize<string[]>(message.ParamsJson) ?? [];
            var digits = message.ToPhone.TrimStart('+');
            var outcome = client.IsConfigured
                ? await client.SendTemplateAsync(digits, WhatsAppTemplates.NameOf(settings.TemplatePrefix, message.Kind), settings.Language, values, ct)
                : new SendOutcome.Rejected("not_configured");

            message.Attempts++;
            switch (outcome)
            {
                case SendOutcome.Accepted accepted:
                    message.Status = MessageStatus.Sent;
                    message.ProviderMessageId = accepted.ProviderMessageId;
                    message.SentAt = clock.GetUtcNow();
                    message.NextAttemptAt = null;
                    break;
                case SendOutcome.Retry retry when message.Attempts < MaxAttempts:
                    message.NextAttemptAt = clock.GetUtcNow().AddMinutes(Math.Pow(2, message.Attempts));
                    log.LogWarning("WhatsApp message {Id} will be tried again: {Reason}", message.Id, retry.Reason);
                    break;
                case SendOutcome.Retry:
                    Fail(message, "unreachable");
                    break;
                case SendOutcome.Rejected rejected:
                    Fail(message, rejected.Code);
                    break;
            }

            // Saved one by one so a crash part way never sends the same message twice.
            await db.SaveChangesAsync(ct);
        }

        return queued.Count;
    }

    private static void Fail(WhatsAppMessage message, string code)
    {
        message.Status = MessageStatus.Failed;
        message.Error = code;
        message.NextAttemptAt = null;
    }
}
