using System.Net;
using System.Text;
using System.Text.Json;
using GuruBook.Modules.Materials.Drive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GuruBook.Api.Tests;

/// <summary>
/// The real Google Drive client against a scripted Google, since the live service cannot be reached from where tests run.
/// This checks what is sent (the scope, the folder, the upload steps) and how each kind of answer is understood.
/// </summary>
public class GoogleDriveClientTests
{
    private sealed record Seen(HttpMethod Method, string Url, string? Authorization, string Body, IReadOnlyDictionary<string, string> Headers);

    private sealed class ScriptedGoogle(Func<Seen, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key, h => string.Join(",", h.Value));
            var body = request.Content is null ? "" : request.Content is StreamContent ? "<stream>" : await request.Content.ReadAsStringAsync(cancellationToken);
            var seen = new Seen(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body, headers);
            Requests.Add(seen);
            return answer(seen);
        }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static string Id() => Guid.NewGuid().ToString("N");

    // Each test uses its own refresh token, because access tokens are cached by refresh token across the process.
    private static HttpResponseMessage Token(Seen seen, string access = "access-1") =>
        seen.Url.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal)
            ? Json(new { access_token = access, expires_in = 3600 })
            : throw new InvalidOperationException("Not a token request: " + seen.Url);

    private static GoogleDriveClient Client(ScriptedGoogle google, TimeProvider? clock = null) =>
        new(new HttpClient(google), Options.Create(new DriveOptions
        {
            ClientId = "client-id", ClientSecret = "client-secret", RedirectUri = "https://api.test/api/materials/drive/callback",
        }), clock ?? TimeProvider.System, NullLogger<GoogleDriveClient>.Instance);

    private static string IdToken(string email) =>
        "h." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { email }))).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".s";

    [Fact]
    public void The_consent_page_asks_for_only_its_own_folder_and_a_long_lived_token()
    {
        var url = new Uri(Client(new ScriptedGoogle(_ => throw new InvalidOperationException())).ConsentUrl("the-state"));
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("accounts.google.com", url.Host);
        Assert.Equal("https://www.googleapis.com/auth/drive.file openid email", query["scope"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("the-state", query["state"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("https://api.test/api/materials/drive/callback", query["redirect_uri"]);
        Assert.DoesNotContain("client-secret", url.ToString());
    }

    [Fact]
    public async Task A_code_becomes_a_refresh_token_and_the_account_email()
    {
        var google = new ScriptedGoogle(_ => Json(new
        {
            access_token = "a", expires_in = 3600, refresh_token = "refresh-A", scope = "openid email https://www.googleapis.com/auth/drive.file",
            id_token = IdToken("teacher@gmail.com"),
        }));
        var grant = await Client(google).ExchangeCodeAsync("the-code", default);

        Assert.Equal("refresh-A", grant.RefreshToken);
        Assert.Equal("teacher@gmail.com", grant.Email);
        var sent = Assert.Single(google.Requests);
        Assert.Contains("grant_type=authorization_code", sent.Body);
        Assert.Contains("code=the-code", sent.Body);
        Assert.Contains("client_secret=client-secret", sent.Body);
    }

    [Fact]
    public async Task A_sign_in_without_the_Drive_permission_or_without_a_refresh_token_is_refused()
    {
        var unticked = new ScriptedGoogle(_ => Json(new { access_token = "a", expires_in = 3600, refresh_token = "r", scope = "openid email" }));
        await Assert.ThrowsAsync<DriveAuthException>(() => Client(unticked).ExchangeCodeAsync("c", default));

        var noRefresh = new ScriptedGoogle(_ => Json(new { access_token = "a", expires_in = 3600, scope = "https://www.googleapis.com/auth/drive.file" }));
        await Assert.ThrowsAsync<DriveAuthException>(() => Client(noRefresh).ExchangeCodeAsync("c", default));

        var badCode = new ScriptedGoogle(_ => Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<DriveAuthException>(() => Client(badCode).ExchangeCodeAsync("c", default));
    }

    [Fact]
    public async Task Google_being_unreachable_is_reported_as_unavailable_not_as_a_crash()
    {
        var down = new ScriptedGoogle(_ => throw new HttpRequestException("no network"));
        await Assert.ThrowsAsync<DriveUnavailableException>(() => Client(down).ExchangeCodeAsync("c", default));
        await Assert.ThrowsAsync<DriveUnavailableException>(() => Client(down).CreateFolderAsync(Id(), "f", null, default));
        await Assert.ThrowsAsync<DriveUnavailableException>(() => Client(down).OpenAsync(Id(), "file", default));
    }

    [Fact]
    public async Task A_folder_is_made_inside_its_parent_and_the_access_token_is_reused_for_the_next_call()
    {
        var refresh = Id();
        var google = new ScriptedGoogle(seen => seen.Url.Contains("oauth2") ? Token(seen) : Json(new { id = "folder-1" }));
        var client = Client(google);

        Assert.Equal("folder-1", await client.CreateFolderAsync(refresh, "Grade 11 Maths", "root-1", default));
        await client.CreateFolderAsync(refresh, "Second", null, default);

        var tokenRequests = google.Requests.Count(r => r.Url.Contains("oauth2"));
        Assert.Equal(1, tokenRequests);
        var create = google.Requests.First(r => r.Url.Contains("/drive/v3/files"));
        Assert.Equal("Bearer access-1", create.Authorization);
        using var body = JsonDocument.Parse(create.Body);
        Assert.Equal("Grade 11 Maths", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("application/vnd.google-apps.folder", body.RootElement.GetProperty("mimeType").GetString());
        Assert.Equal("root-1", body.RootElement.GetProperty("parents")[0].GetString());
        using var second = JsonDocument.Parse(google.Requests.Last().Body);
        Assert.False(second.RootElement.TryGetProperty("parents", out _));
    }

    [Fact]
    public async Task A_stale_access_token_is_replaced_once_and_the_call_goes_through()
    {
        var refresh = Id();
        var tokensIssued = 0;
        var google = new ScriptedGoogle(seen =>
        {
            if (seen.Url.Contains("oauth2"))
            {
                return Token(seen, "access-" + ++tokensIssued);
            }

            return seen.Authorization == "Bearer access-1" ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json(new { id = "folder-9" });
        });

        Assert.Equal("folder-9", await Client(google).CreateFolderAsync(refresh, "f", null, default));
        Assert.Equal(2, tokensIssued);
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":403,\"message\":\"Google Drive API has not been used in project 1 before or it is disabled.\",\"errors\":[{\"reason\":\"accessNotConfigured\"}]}}", "drive_api_disabled")]
    [InlineData("{\"error\":{\"code\":403,\"status\":\"PERMISSION_DENIED\",\"details\":[{\"@type\":\"x\",\"reason\":\"SERVICE_DISABLED\"}]}}", "drive_api_disabled")]
    [InlineData("{\"error\":{\"code\":403,\"errors\":[{\"reason\":\"storageQuotaExceeded\"}]}}", "drive_storage_full")]
    [InlineData("{\"error\":{\"code\":500,\"errors\":[{\"reason\":\"backendError\"}]}}", "drive_unavailable")]
    [InlineData("<html>bad gateway</html>", "drive_unavailable")]
    [InlineData("{\"error\":\"invalid\"}", "drive_unavailable")]
    public async Task A_refusal_from_Drive_is_named_so_the_teacher_knows_what_to_fix(string body, string code)
    {
        var google = new ScriptedGoogle(seen => seen.Url.Contains("oauth2")
            ? Token(seen)
            : new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body) });

        var failure = await Assert.ThrowsAsync<DriveUnavailableException>(() => Client(google).CreateFolderAsync(Id(), "f", null, default));
        Assert.Equal(code, failure.Code);
        Assert.Equal(code, GoogleDriveClient.CodeFor(body));
    }

    [Fact]
    public async Task A_connection_the_teacher_removed_in_Google_is_reported_as_needing_a_new_connection()
    {
        var google = new ScriptedGoogle(_ => Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<DriveAuthException>(() => Client(google).CreateFolderAsync(Id(), "f", null, default));
        await Assert.ThrowsAsync<DriveAuthException>(() => Client(google).OpenAsync(Id(), "file", default));

        var refused = new ScriptedGoogle(seen => seen.Url.Contains("oauth2") ? Token(seen) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<DriveAuthException>(() => Client(refused).CreateFolderAsync(Id(), "f", null, default));
    }

    [Fact]
    public async Task An_upload_announces_the_file_then_sends_the_bytes_to_the_address_Drive_gave()
    {
        var refresh = Id();
        var google = new ScriptedGoogle(seen =>
        {
            if (seen.Url.Contains("oauth2"))
            {
                return Token(seen);
            }

            if (seen.Method == HttpMethod.Post)
            {
                var start = new HttpResponseMessage(HttpStatusCode.OK);
                start.Headers.Location = new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=abc");
                return start;
            }

            return Json(new { id = "file-7", size = "14" });
        });

        using var content = new MemoryStream("%PDF-1.4 hello"u8.ToArray());
        var file = await Client(google).UploadAsync(refresh, "folder-1", "paper.pdf", "application/pdf", content, content.Length, default);

        Assert.Equal("file-7", file.Id);
        Assert.Equal(14, file.SizeBytes);
        var start = google.Requests.Single(r => r.Method == HttpMethod.Post && r.Url.Contains("uploadType=resumable") && !r.Url.Contains("upload_id"));
        Assert.Equal("application/pdf", start.Headers["X-Upload-Content-Type"]);
        Assert.Equal("14", start.Headers["X-Upload-Content-Length"]);
        using var announced = JsonDocument.Parse(start.Body);
        Assert.Equal("paper.pdf", announced.RootElement.GetProperty("name").GetString());
        Assert.Equal("folder-1", announced.RootElement.GetProperty("parents")[0].GetString());
        var put = google.Requests.Single(r => r.Method == HttpMethod.Put);
        Assert.Contains("upload_id=abc", put.Url);
        Assert.Equal("Bearer access-1", put.Authorization);
    }

    [Fact]
    public async Task An_upload_into_a_folder_that_is_gone_says_so()
    {
        var google = new ScriptedGoogle(seen => seen.Url.Contains("oauth2") ? Token(seen) : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var content = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<FolderMissingException>(() => Client(google).UploadAsync(Id(), "gone", "a.pdf", "application/pdf", content, 3, default));
    }

    [Fact]
    public async Task Opening_a_file_streams_it_with_its_type_and_a_missing_file_is_null()
    {
        var refresh = Id();
        var google = new ScriptedGoogle(seen =>
        {
            if (seen.Url.Contains("oauth2"))
            {
                return Token(seen);
            }

            return seen.Url.Contains("/files/there?") ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("bytes"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } } }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = Client(google);

        using (var open = await client.OpenAsync(refresh, "there", default))
        {
            Assert.NotNull(open);
            Assert.Equal("application/pdf", open!.ContentType);
            Assert.Equal(5, open.Length);
            using var reader = new StreamReader(open.Content);
            Assert.Equal("bytes", await reader.ReadToEndAsync());
        }

        Assert.Contains("alt=media", google.Requests.Last().Url);
        Assert.Null(await client.OpenAsync(refresh, "gone", default));
    }

    [Fact]
    public async Task Deleting_a_file_that_is_already_gone_is_fine_and_a_folder_in_the_bin_does_not_count()
    {
        var refresh = Id();
        var google = new ScriptedGoogle(seen =>
            seen.Url.Contains("oauth2") ? Token(seen)
            : seen.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : seen.Url.Contains("/files/binned") ? Json(new { id = "binned", trashed = true })
            : seen.Url.Contains("/files/live") ? Json(new { id = "live", trashed = false })
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = Client(google);

        await client.DeleteAsync(refresh, "already-gone", default);
        Assert.False(await client.FolderExistsAsync(refresh, "binned", default));
        Assert.True(await client.FolderExistsAsync(refresh, "live", default));
        Assert.False(await client.FolderExistsAsync(refresh, "never", default));
    }

    [Fact]
    public async Task A_file_is_renamed_and_moved_by_changing_its_folders_and_a_missing_folder_is_reported()
    {
        var refresh = Id();
        var google = new ScriptedGoogle(seen =>
            seen.Url.Contains("oauth2") ? Token(seen)
            : seen.Url.Contains("/files/gone") || seen.Url.Contains("addParents=nowhere") ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(new { id = "x" }));
        var client = Client(google);

        await client.RenameAsync(refresh, "folder-1", "Term 1", default);
        await client.RenameAsync(refresh, "gone", "Whatever", default);
        var rename = google.Requests.First(r => r.Method == HttpMethod.Patch);
        Assert.Contains("/files/folder-1", rename.Url);
        Assert.Equal("Bearer access-1", rename.Authorization);
        using (var body = JsonDocument.Parse(rename.Body))
        {
            Assert.Equal("Term 1", body.RootElement.GetProperty("name").GetString());
        }

        await client.MoveAsync(refresh, "file-1", "from-1", "to-1", default);
        var move = google.Requests.Last(r => r.Method == HttpMethod.Patch && r.Url.Contains("/files/file-1"));
        Assert.Contains("addParents=to-1", move.Url);
        Assert.Contains("removeParents=from-1", move.Url);
        await Assert.ThrowsAsync<FolderMissingException>(() => client.MoveAsync(refresh, "file-1", "from-1", "nowhere", default));

        var inGone = new ScriptedGoogle(seen => seen.Url.Contains("oauth2") ? Token(seen) : new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<FolderMissingException>(() => Client(inGone).CreateFolderAsync(Id(), "child", "deleted-parent", default));
    }

    [Fact]
    public async Task Revoking_never_throws_even_when_Google_cannot_be_reached()
    {
        await Client(new ScriptedGoogle(_ => throw new HttpRequestException("no network"))).RevokeAsync("refresh-x", default);

        var google = new ScriptedGoogle(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await Client(google).RevokeAsync("refresh-y", default);
        var sent = Assert.Single(google.Requests);
        Assert.StartsWith("https://oauth2.googleapis.com/revoke", sent.Url);
        Assert.Contains("token=refresh-y", sent.Body);
    }
}
