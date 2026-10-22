using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.WhatsApp.Client;

/// <summary>The WhatsApp Cloud API over its plain web API (no Meta library): one template message per call.</summary>
public sealed class MetaWhatsAppClient(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<MetaWhatsAppClient> logger) : IWhatsAppClient
{
    private WhatsAppOptions Options => options.Value;

    public bool IsConfigured => Options.IsConfigured;

    public async Task<SendOutcome> SendTemplateAsync(string toDigits, string template, string language, IReadOnlyList<string> bodyParams, CancellationToken ct)
    {
        var body = new
        {
            messaging_product = "whatsapp",
            to = toDigits,
            type = "template",
            template = new
            {
                name = template,
                language = new { code = language },
                components = new[] { new { type = "body", parameters = bodyParams.Select(p => new { type = "text", text = p }).ToArray() } },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{Options.ApiVersion}/{Options.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new SendOutcome.Retry("WhatsApp could not be reached.");
        }

        using var _ = response;
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
                    && messages[0].TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } providerId)
                {
                    return new SendOutcome.Accepted(providerId);
                }
            }
            catch (JsonException)
            {
                // Falls through to the retry below.
            }

            return new SendOutcome.Retry("WhatsApp sent no message id.");
        }

        var code = ErrorCodeOf(text);
        logger.LogWarning("WhatsApp refused a message: HTTP {Status}, Meta error {Code}", (int)response.StatusCode, code);
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests || IsBusy(code))
        {
            return new SendOutcome.Retry($"WhatsApp is busy ({code}).");
        }

        return new SendOutcome.Rejected(Errors.Describe(code));
    }

    // Meta's own throttling codes: too many calls, too many to one number, temporary problem.
    private static bool IsBusy(int? code) => code is 4 or 2 or 80007 or 130429 or 131056 or 131000;

    private static int? ErrorCodeOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code) && code.TryGetInt32(out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Turns Meta's numeric error codes into the short codes the web app has words for.</summary>
public static class Errors
{
    public static string Describe(int? metaCode) => metaCode switch
    {
        131026 => "not_on_whatsapp",
        131049 => "not_delivered",
        132000 or 132001 or 132005 or 132007 or 132012 or 132015 or 132016 => "template_problem",
        190 or 10 or >= 200 and <= 299 => "auth_problem",
        _ => "failed",
    };
}
