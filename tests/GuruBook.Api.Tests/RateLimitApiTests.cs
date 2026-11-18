using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace GuruBook.Api.Tests;

/// <summary>The API with small limits, so tests can reach them.</summary>
public sealed class TightLimitsApiFactory : ApiFactory
{
    public const int PasswordsPerMinute = 3;
    public const int UserPerMinute = 40;

    protected override void ConfigureSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("RateLimit:PasswordsPerMinute", PasswordsPerMinute.ToString());
        builder.UseSetting("RateLimit:UserPerMinute", UserPerMinute.ToString());
    }
}

/// <summary>How often a signed-in person may call the API, and the tighter limit on passwords.</summary>
public class RateLimitApiTests(TightLimitsApiFactory factory) : IClassFixture<TightLimitsApiFactory>
{
    private static async Task<HttpResponseMessage> WrongPassword(HttpClient client) =>
        await client.PostAsJsonAsync("/api/identity/change-password", new { currentPassword = "not-my-password", newPassword = "Another-pass-1" });

    [Fact]
    public async Task Password_changes_are_limited_per_person_and_say_when_to_try_again()
    {
        var hasty = await factory.NewTeacherAsync("Hasty");
        var calm = await factory.NewTeacherAsync("Calm");

        HttpResponseMessage? limited = null;
        for (var i = 0; i < TightLimitsApiFactory.PasswordsPerMinute + 1 && limited is null; i++)
        {
            var response = await WrongPassword(hasty.Client);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
            else
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }

        Assert.NotNull(limited);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // Someone else is not slowed down by it, and the hasty teacher can still use the rest of the app.
        Assert.Equal(HttpStatusCode.BadRequest, (await WrongPassword(calm.Client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hasty.Client.GetAsync("/api/identity/me")).StatusCode);
    }

    [Fact]
    public async Task Each_signed_in_person_has_a_limit_across_the_whole_API()
    {
        var busy = await factory.NewTeacherAsync("Busy");
        var other = await factory.NewTeacherAsync("Other");

        var answered = 0;
        HttpStatusCode last;
        do
        {
            last = (await busy.Client.GetAsync("/api/identity/me")).StatusCode;
            if (last == HttpStatusCode.OK)
            {
                answered++;
            }
        }
        while (last == HttpStatusCode.OK && answered <= TightLimitsApiFactory.UserPerMinute);

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        Assert.InRange(answered, 1, TightLimitsApiFactory.UserPerMinute);

        Assert.Equal(HttpStatusCode.OK, (await other.Client.GetAsync("/api/identity/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);
    }
}
