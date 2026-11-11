using System.Net;
using System.Net.Http.Json;

namespace GuruBook.Api.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record SystemInfo(string Name, string Version, string Environment, string[] Modules);

    [Fact]
    public async Task Health_endpoint_reports_the_process_is_up()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Every_answer_tells_browsers_not_to_sniff_frame_or_run_it()
    {
        var response = await factory.CreateClient().GetAsync("/api/system/info");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task System_info_lists_every_module()
    {
        var client = factory.CreateClient();

        var info = await client.GetFromJsonAsync<SystemInfo>("/api/system/info");

        Assert.NotNull(info);
        Assert.Equal("GuruBook", info.Name);
        Assert.Equal(
            ["identity", "catalog", "classes", "students", "fees", "attendance", "reports", "exams", "notifications", "dashboard", "plans", "materials", "whatsapp", "sms", "messaging", "payments", "privacy"],
            info.Modules);
    }
}
