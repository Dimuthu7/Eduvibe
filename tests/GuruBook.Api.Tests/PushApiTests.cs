using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuruBook.Modules.Notifications;
using GuruBook.Modules.Notifications.Push;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Api.Tests;

/// <summary>Phone notifications: turning them on, who they go to, that repeats do not buzz twice, and that the encryption follows the standard.</summary>
public class PushApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>What a browser hands over when it subscribes: its own address, a public key and an auth secret.</summary>
    private static object NewDevice(string endpoint, string language = "en")
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = key.ExportParameters(false).Q;
        byte[] publicKey = [0x04, .. point.X!, .. point.Y!];
        return new { endpoint, p256dh = WebPushCrypto.ToBase64Url(publicKey), auth = WebPushCrypto.ToBase64Url(RandomNumberGenerator.GetBytes(16)), language };
    }

    private static string NewEndpoint() => $"https://push.example.test/send/{Guid.NewGuid():N}";

    private async Task Notify(Guid recipient, string kind, string? onceKey = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotifier>()
            .NotifyAsync([new NotifyRequest(recipient, kind, new { classTitle = "Maths" }, Link: "/welcome", OnceKey: onceKey)]);
    }

    private async Task<int> Devices(string endpoint)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().PushDevices.CountAsync(d => d.Endpoint == endpoint);
    }

    [Fact]
    public async Task The_app_is_told_whether_phone_notifications_are_available_and_the_key_to_use()
    {
        var teacher = await factory.NewTeacherAsync();
        var on = await Json(await teacher.Client.GetAsync("/api/notifications/push"));
        Assert.True(on.GetProperty("enabled").GetBoolean());
        Assert.Equal("test-public-key", on.GetProperty("publicKey").GetString());

        factory.Push.Enabled = false;
        try
        {
            var off = await Json(await teacher.Client.GetAsync("/api/notifications/push"));
            Assert.False(off.GetProperty("enabled").GetBoolean());
            Assert.Equal("", off.GetProperty("publicKey").GetString());
            var refused = await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(NewEndpoint()));
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        }
        finally
        {
            factory.Push.Enabled = true;
        }
    }

    [Fact]
    public async Task Only_signed_in_people_can_manage_devices()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/notifications/push")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PutAsJsonAsync("/api/notifications/push/device", NewDevice(NewEndpoint()))).StatusCode);
    }

    [Theory]
    [InlineData("http://insecure.example.test/x")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task A_device_needs_a_secure_address(string endpoint)
    {
        var teacher = await factory.NewTeacherAsync();
        var response = await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("push_device_invalid", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_device_with_broken_keys_is_refused()
    {
        var teacher = await factory.NewTeacherAsync();
        var response = await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", new { endpoint = NewEndpoint(), p256dh = "abc", auth = "def", language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_teacher_gets_a_phone_notification_for_a_new_notification_in_their_language()
    {
        var teacher = await factory.NewTeacherAsync();
        var endpoint = NewEndpoint();
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint, "si"))).StatusCode);

        await Notify(teacher.Id, "fees_unpaid", onceKey: "t:" + Guid.NewGuid());

        var sent = Assert.Single(factory.Push.SentTo(endpoint));
        var payload = JsonDocument.Parse(sent.Payload).RootElement;
        Assert.Equal("fees_unpaid", payload.GetProperty("kind").GetString());
        Assert.Equal("si", payload.GetProperty("lang").GetString());
        Assert.Equal("/welcome", payload.GetProperty("link").GetString());
        Assert.Equal("Maths", payload.GetProperty("params").GetProperty("classTitle").GetString());
    }

    [Fact]
    public async Task Asking_for_the_same_notification_again_does_not_buzz_the_phone_twice()
    {
        var teacher = await factory.NewTeacherAsync();
        var endpoint = NewEndpoint();
        await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint));
        var key = "once:" + Guid.NewGuid();

        await Notify(teacher.Id, "fees_unpaid", key);
        await Notify(teacher.Id, "fees_unpaid", key);

        Assert.Single(factory.Push.SentTo(endpoint));
    }

    [Fact]
    public async Task Nobody_else_gets_it()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync();
        var mine = NewEndpoint();
        var theirs = NewEndpoint();
        await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(mine));
        await other.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(theirs));

        await Notify(teacher.Id, "fees_unpaid", "k:" + Guid.NewGuid());

        Assert.Single(factory.Push.SentTo(mine));
        Assert.Empty(factory.Push.SentTo(theirs));
    }

    [Fact]
    public async Task A_phone_signed_in_as_someone_else_moves_to_them()
    {
        var first = await factory.NewTeacherAsync();
        var second = await factory.NewTeacherAsync();
        var endpoint = NewEndpoint();
        await first.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint));
        await second.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint));
        Assert.Equal(1, await Devices(endpoint));

        await Notify(first.Id, "fees_unpaid", "k:" + Guid.NewGuid());
        Assert.Empty(factory.Push.SentTo(endpoint));
        await Notify(second.Id, "fees_unpaid", "k:" + Guid.NewGuid());
        Assert.Single(factory.Push.SentTo(endpoint));
    }

    [Fact]
    public async Task Turning_notifications_off_stops_them_and_cannot_remove_someone_elses_device()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync();
        var endpoint = NewEndpoint();
        await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint));

        await other.Client.PostAsJsonAsync("/api/notifications/push/unsubscribe", new { endpoint });
        Assert.Equal(1, await Devices(endpoint));

        await teacher.Client.PostAsJsonAsync("/api/notifications/push/unsubscribe", new { endpoint });
        Assert.Equal(0, await Devices(endpoint));
        await Notify(teacher.Id, "fees_unpaid", "k:" + Guid.NewGuid());
        Assert.Empty(factory.Push.SentTo(endpoint));
    }

    [Fact]
    public async Task A_device_the_push_service_no_longer_knows_is_forgotten()
    {
        var teacher = await factory.NewTeacherAsync();
        var endpoint = NewEndpoint();
        await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint));

        factory.Push.Outcome = PushOutcome.Gone;
        try
        {
            await Notify(teacher.Id, "fees_unpaid", "k:" + Guid.NewGuid());
        }
        finally
        {
            factory.Push.Outcome = PushOutcome.Sent;
        }

        Assert.Equal(0, await Devices(endpoint));
    }

    [Fact]
    public async Task A_temporary_push_failure_keeps_the_device_and_the_notification()
    {
        var teacher = await factory.NewTeacherAsync();
        var endpoint = NewEndpoint();
        await teacher.Client.PutAsJsonAsync("/api/notifications/push/device", NewDevice(endpoint));

        factory.Push.Outcome = PushOutcome.Failed;
        try
        {
            await Notify(teacher.Id, "fees_unpaid", "k:" + Guid.NewGuid());
        }
        finally
        {
            factory.Push.Outcome = PushOutcome.Sent;
        }

        Assert.Equal(1, await Devices(endpoint));
        var list = await Json(await teacher.Client.GetAsync("/api/notifications"));
        Assert.Equal(1, list.GetProperty("unread").GetInt32());
    }

    // RFC 8291 section 5: the standard's own example, with its fixed keys and salt, must give its exact message.
    [Fact]
    public void Encryption_matches_the_example_in_the_standard()
    {
        var plaintext = Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon");
        var devicePublic = WebPushCrypto.FromBase64Url("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var auth = WebPushCrypto.FromBase64Url("BTBZMqHH6r4Tts7J_aSIgg");
        var salt = WebPushCrypto.FromBase64Url("DGv6ra1nlYgDCS1FRnbzlw");
        var senderPublic = WebPushCrypto.FromBase64Url("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = WebPushCrypto.FromBase64Url("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"),
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..65] },
        });

        var body = WebPushCrypto.Encrypt(plaintext, devicePublic, auth, sender, salt);

        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebPushCrypto.ToBase64Url(body));
    }

    [Fact]
    public void The_sender_signs_its_identity_so_the_push_service_can_check_it()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        byte[] publicBytes = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
        var publicKey = WebPushCrypto.ToBase64Url(publicBytes);
        var now = new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);

        var header = WebPushCrypto.VapidAuthorization("https://fcm.example.test/send/abc", "https://gurubook.example.test", publicKey, WebPushCrypto.ToBase64Url(parameters.D!), now);

        Assert.StartsWith("vapid t=", header);
        Assert.EndsWith($", k={publicKey}", header);
        var jwt = header["vapid t=".Length..header.IndexOf(", k=", StringComparison.Ordinal)].Split('.');
        var claims = JsonDocument.Parse(WebPushCrypto.FromBase64Url(jwt[1])).RootElement;
        Assert.Equal("https://fcm.example.test", claims.GetProperty("aud").GetString());
        Assert.Equal("https://gurubook.example.test", claims.GetProperty("sub").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), WebPushCrypto.FromBase64Url(jwt[2]), HashAlgorithmName.SHA256));
    }
}
