using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.Sms.Client;

/// <summary>
/// text.lk over its plain web API (no library): one text per call to /api/v3/sms/send. It is a Sri Lankan provider, paid per message part,
/// so a refused message is never tried again for the same reason.
/// </summary>
public sealed class TextLkSmsClient(HttpClient http, IOptions<SmsOptions> options, ILogger<TextLkSmsClient> logger) : ISmsClient
{
    private SmsOptions Options => options.Value;

    public bool IsConfigured => Options.IsConfigured;

    public async Task<SmsOutcome> SendAsync(string toDigits, string text, CancellationToken ct)
    {
        var body = new
        {
            recipient = toDigits,
            sender_id = Options.SenderId,
            type = "plain",
            message = text,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Options.BaseUrl.TrimEnd('/')}/api/v3/sms/send")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new SmsOutcome.Retry("The SMS provider could not be reached.");
        }

        using var _ = response;
        var answer = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode && IdOf(answer) is { } providerId)
        {
            return new SmsOutcome.Accepted(providerId);
        }

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new SmsOutcome.Retry($"The SMS provider is busy (HTTP {(int)response.StatusCode}).");
        }

        var code = Describe(response.StatusCode, answer);
        logger.LogWarning("The SMS provider refused a message: HTTP {Status}, {Code}", (int)response.StatusCode, code);
        return response.IsSuccessStatusCode ? new SmsOutcome.Retry("The SMS provider sent no message id.") : new SmsOutcome.Rejected(code);
    }

    /// <summary>text.lk answers {"status":"success","data":{"uid":"..."}}; anything else means it did not take the message.</summary>
    private static string? IdOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() is "error" or "failed")
            {
                return null;
            }

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in (string[])["uid", "message_id", "id"])
                {
                    if (data.TryGetProperty(name, out var id) && Text(id) is { Length: > 0 } value)
                    {
                        return value;
                    }
                }
            }

            return root.TryGetProperty("uid", out var top) && Text(top) is { Length: > 0 } only ? only : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            _ => null,
        };

    /// <summary>Turns what the provider said into one of the short codes the web app has words for.</summary>
    public static string Describe(HttpStatusCode status, string answer)
    {
        var said = answer.ToLowerInvariant();
        return status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "auth_problem"
            : HasAny(said, "insufficient", "no credit", "low balance", "balance") ? "no_credit"
            : HasAny(said, "sender", "mask") ? "sender_problem"
            : HasAny(said, "recipient", "invalid number", "number is invalid", "msisdn") ? "bad_number"
            : "failed";
    }

    private static bool HasAny(string text, params string[] words) => words.Any(text.Contains);
}
