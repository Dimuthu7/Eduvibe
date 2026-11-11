using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.Notifications.Push;

/// <summary>Sends to a browser's push service (Google, Mozilla, Apple), which delivers it to the phone even when the app is closed.</summary>
internal sealed class WebPushSender(HttpClient http, IOptions<PushOptions> options, TimeProvider clock, ILogger<WebPushSender> log) : IPushSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public bool Enabled => options.Value.Enabled;

    public string PublicKey => options.Value.PublicKey;

    public async Task<PushOutcome> SendAsync(PushTarget target, string payloadJson, CancellationToken cancellationToken)
    {
        if (!Enabled || !Uri.TryCreate(target.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return PushOutcome.Failed;
        }

        // A device saved before addresses were checked, on a host that is not a push service: never post to it, and forget it.
        if (!options.Value.IsPushService(endpoint))
        {
            return PushOutcome.Gone;
        }

        try
        {
            var settings = options.Value;
            var body = WebPushCrypto.Encrypt(Encoding.UTF8.GetBytes(payloadJson), WebPushCrypto.FromBase64Url(target.P256dh), WebPushCrypto.FromBase64Url(target.Auth));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");
            request.Headers.TryAddWithoutValidation("Authorization", WebPushCrypto.VapidAuthorization(target.Endpoint, settings.Subject, settings.PublicKey, settings.PrivateKey, clock.GetUtcNow()));
            request.Headers.TryAddWithoutValidation("TTL", "86400");
            request.Headers.TryAddWithoutValidation("Urgency", "normal");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return PushOutcome.Sent;
            }

            // 404 and 410 mean the subscription no longer exists; anything else may pass.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return PushOutcome.Gone;
            }

            log.LogWarning("Push service answered {Status} for {Host}", (int)response.StatusCode, endpoint.Host);
            return PushOutcome.Failed;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            log.LogWarning(e, "Could not send a push notification to {Host}", endpoint.Host);
            return PushOutcome.Failed;
        }
    }
}
