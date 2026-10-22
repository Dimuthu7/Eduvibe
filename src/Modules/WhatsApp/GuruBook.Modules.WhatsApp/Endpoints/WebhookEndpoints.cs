using GuruBook.Modules.WhatsApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GuruBook.Modules.WhatsApp.Endpoints;

/// <summary>The two calls Meta makes: once to check we own this address, then for every delivery report and reply. Neither has a signed-in user.</summary>
internal static class WebhookEndpoints
{
    private const int MaxBody = 1_000_000;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/webhook", Verify).AllowAnonymous();
        group.MapPost("/webhook", Receive).AllowAnonymous();
    }

    // Meta sends hub.mode, hub.verify_token and hub.challenge, and wants the challenge back when the token is ours.
    private static IResult Verify(HttpRequest request, IOptions<WhatsAppOptions> options)
    {
        var token = options.Value.VerifyToken;
        return token != "" && request.Query["hub.mode"] == "subscribe" && request.Query["hub.verify_token"] == token
            ? Results.Text(request.Query["hub.challenge"].ToString())
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> Receive(HttpRequest request, WhatsAppWebhook webhook, ILogger<WhatsAppWebhook> log, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBody)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (!webhook.IsValidSignature(body, request.Headers["X-Hub-Signature-256"]))
        {
            return Results.Unauthorized();
        }

        try
        {
            await webhook.HandleAsync(body, ct);
        }
        catch (System.Text.Json.JsonException e)
        {
            log.LogWarning(e, "A WhatsApp webhook call was not valid JSON");
            return Results.BadRequest();
        }

        return Results.Ok();
    }
}
