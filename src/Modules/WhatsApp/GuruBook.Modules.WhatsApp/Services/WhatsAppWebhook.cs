using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuruBook.Modules.WhatsApp.Client;
using GuruBook.Modules.WhatsApp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.WhatsApp.Services;

/// <summary>
/// What Meta tells us afterwards: a message was delivered, read or failed, or a parent wrote back. Calls come from Meta, not a teacher,
/// so the teacher filter is lifted and the call is trusted only when its signature matches the app secret.
/// </summary>
public sealed class WhatsAppWebhook(WhatsAppDbContext db, IOptions<WhatsAppOptions> options, TimeProvider clock)
{
    /// <summary>How far back a STOP reaches: teachers who messaged the number in this long are told to leave it alone.</summary>
    public static readonly TimeSpan OptOutReach = TimeSpan.FromDays(90);

    private static readonly string[] StopWords = ["STOP", "UNSUBSCRIBE", "STOP ALL"];
    private static readonly string[] StartWords = ["START", "SUBSCRIBE"];

    public bool IsValidSignature(string body, string? header)
    {
        var secret = options.Value.AppSecret;
        if (secret == "" || header is null || !header.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(header["sha256=".Length..]));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public async Task HandleAsync(string body, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(body);
        foreach (var value in Values(doc.RootElement))
        {
            if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
            {
                foreach (var status in statuses.EnumerateArray())
                {
                    await ApplyStatusAsync(status, ct);
                }
            }

            if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    await ApplyReplyAsync(message, ct);
                }
            }
        }
    }

    private static IEnumerable<JsonElement> Values(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("value", out var value))
                {
                    yield return value;
                }
            }
        }
    }

    private async Task ApplyStatusAsync(JsonElement status, CancellationToken ct)
    {
        if (!status.TryGetProperty("id", out var idValue) || idValue.GetString() is not { } id
            || !status.TryGetProperty("status", out var kind) || kind.GetString() is not { } name)
        {
            return;
        }

        var message = await db.Messages.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.ProviderMessageId == id, ct);
        if (message is null)
        {
            return;
        }

        switch (name)
        {
            case "sent" when message.Status == MessageStatus.Queued:
                message.Status = MessageStatus.Sent;
                break;
            case "delivered" when message.Status is MessageStatus.Queued or MessageStatus.Sent:
                message.Status = MessageStatus.Delivered;
                break;
            case "read" when message.Status != MessageStatus.Failed:
                message.Status = MessageStatus.Read;
                break;
            case "failed" when message.Status is not (MessageStatus.Delivered or MessageStatus.Read):
                message.Status = MessageStatus.Failed;
                message.Error = Errors.Describe(FirstErrorCode(status));
                break;
            default:
                return;
        }

        await db.SaveChangesAsync(ct);
    }

    private static int? FirstErrorCode(JsonElement status) =>
        status.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
            && errors[0].TryGetProperty("code", out var code) && code.TryGetInt32(out var value) ? value : null;

    // A parent writes STOP to a teacher's messages: no teacher who messaged that number lately may message it again, until the parent writes START.
    private async Task ApplyReplyAsync(JsonElement message, CancellationToken ct)
    {
        if (!message.TryGetProperty("from", out var from) || from.GetString() is not { Length: > 0 } digits
            || !message.TryGetProperty("text", out var text) || !text.TryGetProperty("body", out var bodyValue) || bodyValue.GetString() is not { } reply)
        {
            return;
        }

        var phone = "+" + digits.TrimStart('+');
        var word = reply.Trim().TrimEnd('.', '!').ToUpperInvariant();
        if (StopWords.Contains(word))
        {
            var since = clock.GetUtcNow() - OptOutReach;
            var tenants = (await db.Messages.IgnoreQueryFilters().Where(m => m.ToPhone == phone).Select(m => new { m.TenantId, m.CreatedAt }).ToListAsync(ct))
                .Where(m => m.CreatedAt >= since).Select(m => m.TenantId).Distinct().ToList();
            var already = (await db.OptOuts.IgnoreQueryFilters().Where(o => o.Phone == phone).Select(o => o.TenantId).ToListAsync(ct)).ToHashSet();
            foreach (var tenant in tenants.Where(t => !already.Contains(t)))
            {
                db.OptOuts.Add(new WhatsAppOptOut { TenantId = tenant, Phone = phone });
            }

            await db.SaveChangesAsync(ct);
        }
        else if (StartWords.Contains(word))
        {
            await db.OptOuts.IgnoreQueryFilters().Where(o => o.Phone == phone).ExecuteDeleteAsync(ct);
        }
    }
}
