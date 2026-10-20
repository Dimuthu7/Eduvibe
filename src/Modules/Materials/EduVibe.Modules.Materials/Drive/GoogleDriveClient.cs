using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduVibe.Modules.Materials.Drive;

/// <summary>Google Drive over its plain web API (no Google library), limited to the drive.file scope.</summary>
public sealed class GoogleDriveClient(HttpClient http, IOptions<DriveOptions> options, TimeProvider clock, ILogger<GoogleDriveClient> logger) : IDriveClient
{
    private const string Scope = "https://www.googleapis.com/auth/drive.file";
    private const string FolderType = "application/vnd.google-apps.folder";

    // A teacher's access token is good for about an hour, so it is reused between requests instead of asked for each time.
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> AccessTokens = new();

    private DriveOptions Options => options.Value;

    public bool IsConfigured => Options.IsConfigured;

    public string ConsentUrl(string state) =>
        "https://accounts.google.com/o/oauth2/v2/auth?" + Query(new()
        {
            ["client_id"] = Options.ClientId,
            ["redirect_uri"] = Options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = $"{Scope} openid email",
            // offline + consent so Google always hands back a refresh token, even when the teacher connected before.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
        });

    public async Task<DriveGrant> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await PostFormAsync("https://oauth2.googleapis.com/token", new()
            {
                ["code"] = code,
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["redirect_uri"] = Options.RedirectUri,
                ["grant_type"] = "authorization_code",
            }, ct);
        }
        catch (HttpRequestException e)
        {
            throw new DriveUnavailableException("Google could not be reached.", e);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            throw new DriveAuthException($"Google refused the sign-in code ({(int)response.StatusCode}).");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct) ?? throw new DriveAuthException("Google sent no token.");
        if (string.IsNullOrEmpty(token.RefreshToken) || token.Scope?.Contains(Scope, StringComparison.Ordinal) != true)
        {
            // The teacher unticked the Drive permission on Google's page, or Google sent no long-lived token.
            throw new DriveAuthException("The Drive permission was not given.");
        }

        return new DriveGrant(token.RefreshToken, EmailOf(token.IdToken));
    }

    public async Task<string> CreateFolderAsync(string refreshToken, string name, string? parentId, CancellationToken ct)
    {
        var body = new Dictionary<string, object> { ["name"] = name, ["mimeType"] = FolderType };
        if (parentId is not null)
        {
            body["parents"] = new[] { parentId };
        }

        using var response = await SendAsync(refreshToken, () => new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id")
        {
            Content = JsonContent.Create(body),
        }, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<IdResponse>(ct))?.Id ?? throw new DriveUnavailableException("Drive sent no folder id.");
    }

    public async Task<bool> FolderExistsAsync(string refreshToken, string folderId, CancellationToken ct)
    {
        using var response = await SendAsync(refreshToken, () => new HttpRequestMessage(HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(folderId)}?fields=id,trashed"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<IdResponse>(ct))?.Trashed != true;
    }

    public async Task<DriveFile> UploadAsync(
        string refreshToken, string folderId, string name, string contentType, Stream content, long length, CancellationToken ct)
    {
        // Resumable upload in one go: first say what is coming, then send the bytes straight from the request.
        using var start = await SendAsync(refreshToken, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,size")
            {
                Content = JsonContent.Create(new { name, parents = new[] { folderId } }),
            };
            request.Headers.Add("X-Upload-Content-Type", contentType);
            request.Headers.Add("X-Upload-Content-Length", length.ToString());
            return request;
        }, ct);
        if (start.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FolderMissingException();
        }

        await EnsureSuccessAsync(start, ct);
        var location = start.Headers.Location ?? throw new DriveUnavailableException("Drive sent no upload address.");

        var token = await AccessTokenAsync(refreshToken, ct);
        using var put = new HttpRequestMessage(HttpMethod.Put, location) { Content = new StreamContent(content) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        put.Content.Headers.ContentLength = length;
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage done;
        try
        {
            done = await http.SendAsync(put, ct);
        }
        catch (HttpRequestException e)
        {
            throw new DriveUnavailableException("Drive could not be reached.", e);
        }

        using var _ = done;
        await EnsureSuccessAsync(done, ct);
        var saved = await done.Content.ReadFromJsonAsync<IdResponse>(ct) ?? throw new DriveUnavailableException("Drive sent no file id.");
        return new DriveFile(saved.Id, long.TryParse(saved.Size, out var size) ? size : length);
    }

    public async Task<DriveDownload?> OpenAsync(string refreshToken, string fileId, CancellationToken ct)
    {
        var token = await AccessTokenAsync(refreshToken, ct);
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException e)
        {
            request.Dispose();
            throw new DriveUnavailableException("Drive could not be reached.", e);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            request.Dispose();
            return null;
        }

        try
        {
            await EnsureSuccessAsync(response, ct);
        }
        catch
        {
            response.Dispose();
            request.Dispose();
            throw;
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return new DriveDownload(stream, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", response.Content.Headers.ContentLength, new Both(response, request));
    }

    public async Task DeleteAsync(string refreshToken, string fileId, CancellationToken ct)
    {
        using var response = await SendAsync(refreshToken, () => new HttpRequestMessage(HttpMethod.Delete,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}"), ct);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, ct);
        }
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            using var response = await PostFormAsync("https://oauth2.googleapis.com/revoke", new() { ["token"] = refreshToken }, ct);
            AccessTokens.TryRemove(Key(refreshToken), out _);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(e, "Could not tell Google to revoke a Drive token");
        }
    }

    // Sends a request for a teacher. A 401 means the cached access token went stale, so one retry with a fresh one.
    private async Task<HttpResponseMessage> SendAsync(string refreshToken, Func<HttpRequestMessage> build, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await AccessTokenAsync(refreshToken, ct, forceNew: attempt > 0);
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (HttpRequestException e)
            {
                throw new DriveUnavailableException("Drive could not be reached.", e);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                continue;
            }

            return response;
        }
    }

    private async Task<string> AccessTokenAsync(string refreshToken, CancellationToken ct, bool forceNew = false)
    {
        var key = Key(refreshToken);
        if (!forceNew && AccessTokens.TryGetValue(key, out var cached) && cached.Expires > clock.GetUtcNow().AddMinutes(1))
        {
            return cached.Token;
        }

        HttpResponseMessage response;
        try
        {
            response = await PostFormAsync("https://oauth2.googleapis.com/token", new()
            {
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
            }, ct);
        }
        catch (HttpRequestException e)
        {
            throw new DriveUnavailableException("Google could not be reached.", e);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                // invalid_grant: the teacher removed EduVibe from their Google account, or the grant lapsed.
                AccessTokens.TryRemove(key, out _);
                throw new DriveAuthException("Google no longer accepts this connection.");
            }

            await EnsureSuccessAsync(response, ct);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct) ?? throw new DriveUnavailableException("Google sent no token.");
            AccessTokens[key] = (token.AccessToken, clock.GetUtcNow().AddSeconds(token.ExpiresIn));
            return token.AccessToken;
        }
    }

    private Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct) =>
        http.PostAsync(url, new FormUrlEncodedContent(form), ct);

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new DriveAuthException("Drive refused the connection.");
        }

        var detail = await response.Content.ReadAsStringAsync(ct);
        logger.LogWarning("Google Drive answered {Status}: {Detail}", (int)response.StatusCode, detail.Length > 300 ? detail[..300] : detail);
        throw new DriveUnavailableException($"Drive answered {(int)response.StatusCode}.", code: CodeFor(detail));
    }

    /// <summary>
    /// What Google's error body says, as an error code the person can act on. A project that never enabled the Drive API answers
    /// 403 accessNotConfigured (or SERVICE_DISABLED), which looked like "Drive is down" until it was named.
    /// </summary>
    public static string CodeFor(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("error", out var error))
            {
                return DriveUnavailableException.Generic;
            }

            var reasons = new List<string>();
            foreach (var list in new[] { "errors", "details" })
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    reasons.AddRange(items.EnumerateArray()
                        .Select(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("reason", out var r) ? r.GetString() : null)
                        .OfType<string>());
                }
            }

            return reasons switch
            {
                _ when reasons.Contains("accessNotConfigured") || reasons.Contains("SERVICE_DISABLED") => "drive_api_disabled",
                _ when reasons.Contains("storageQuotaExceeded") => "drive_storage_full",
                _ => DriveUnavailableException.Generic,
            };
        }
        catch (JsonException)
        {
            return DriveUnavailableException.Generic;
        }
    }

    private static string Key(string refreshToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private static string Query(Dictionary<string, string> values) =>
        string.Join("&", values.Select(v => $"{Uri.EscapeDataString(v.Key)}={Uri.EscapeDataString(v.Value)}"));

    // The email is in the id token Google sent straight to us over TLS, so it is read, not verified.
    private static string? EmailOf(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')));
            return json.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    private sealed class Both(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("scope")] string? Scope,
        [property: System.Text.Json.Serialization.JsonPropertyName("id_token")] string? IdToken);

    private sealed record IdResponse(string Id, bool? Trashed, string? Size);
}

/// <summary>The class's Drive folder was deleted in Drive. The caller makes a new one and tries again.</summary>
public sealed class FolderMissingException : Exception;
