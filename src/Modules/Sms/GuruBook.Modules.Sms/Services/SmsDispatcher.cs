using GuruBook.Modules.Sms.Client;
using GuruBook.Modules.Sms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GuruBook.Modules.Sms.Services;

/// <summary>
/// Sends the queued texts. It acts for no teacher (it runs in the background), so it lifts the teacher filter and works from what the log holds.
/// A busy provider is tried again after 2, 4, 8 minutes; after that, or when the provider refuses the text for good, it is marked Failed and
/// stops counting against the allowance. A refused text is never tried again, because every attempt is paid for.
/// </summary>
public sealed class SmsDispatcher(SmsDbContext db, ISmsClient client, TimeProvider clock, ILogger<SmsDispatcher> log)
{
    public const int MaxAttempts = 4;
    private const int BatchSize = 50;

    /// <summary>Sends what is due, oldest first, and returns how many texts were handled.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // SQLite (used in tests) cannot compare dates in a query, and the queue is short, so due messages are picked after loading.
        var queued = (await db.Messages.IgnoreQueryFilters().Where(m => m.Status == SmsStatus.Queued).OrderBy(m => m.Id).ToListAsync(ct))
            .Where(m => m.NextAttemptAt is null || m.NextAttemptAt <= now)
            .Take(BatchSize)
            .ToList();

        foreach (var message in queued)
        {
            var outcome = client.IsConfigured
                ? await client.SendAsync(message.ToPhone.TrimStart('+'), message.Text, ct)
                : new SmsOutcome.Rejected("not_configured");

            message.Attempts++;
            switch (outcome)
            {
                case SmsOutcome.Accepted accepted:
                    message.Status = SmsStatus.Sent;
                    message.ProviderMessageId = accepted.ProviderMessageId;
                    message.SentAt = clock.GetUtcNow();
                    message.NextAttemptAt = null;
                    log.LogInformation("SMS {Id} sent ({Parts} part(s))", message.Id, message.Parts);
                    break;
                case SmsOutcome.Retry retry when message.Attempts < MaxAttempts:
                    message.NextAttemptAt = clock.GetUtcNow().AddMinutes(Math.Pow(2, message.Attempts));
                    log.LogWarning("SMS {Id} will be tried again: {Reason}", message.Id, retry.Reason);
                    break;
                case SmsOutcome.Retry:
                    Fail(message, "unreachable");
                    log.LogWarning("SMS {Id} failed: unreachable", message.Id);
                    break;
                case SmsOutcome.Rejected rejected:
                    Fail(message, rejected.Code);
                    log.LogWarning("SMS {Id} failed: {Code}", message.Id, rejected.Code);
                    break;
            }

            // Saved one by one so a crash part way never sends the same text twice.
            await db.SaveChangesAsync(ct);
        }

        return queued.Count;
    }

    private static void Fail(SmsMessage message, string code)
    {
        message.Status = SmsStatus.Failed;
        message.Error = code;
        message.NextAttemptAt = null;
    }
}
