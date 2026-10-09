using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EduVibe.Api.Tests;

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
    public async Task System_info_lists_every_module()
    {
        var client = factory.CreateClient();

        var info = await client.GetFromJsonAsync<SystemInfo>("/api/system/info");

        Assert.NotNull(info);
        Assert.Equal("EduVibe", info.Name);
        Assert.Equal(
            ["identity", "classes", "students", "fees", "attendance", "exams", "plans"],
            info.Modules);
    }
}

/// <summary>Runs the real API without a database: the checked endpoints do not need one.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
    }
}
