using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GuruBook.Api.Tests;

/// <summary>What the Super Admin sees across all teachers: the dashboard numbers and the audit log with its filters.</summary>
public class AdminApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task The_dashboard_counts_teachers_institutes_classes_students_and_teachers_per_plan()
    {
        var teacher = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        var venue = (await Json(await teacher.Client.PostAsJsonAsync("/api/classes/venues", new { name = "Hall", district = "Kandy" }))).GetProperty("id").GetGuid();
        var created = await teacher.Client.PostAsJsonAsync("/api/classes/classes", new
        {
            title = "Dash", subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId = venue, monthlyFee = 100m, slots = new[] { new { day = 3, start = "16:00", end = "18:00" } },
        });
        var classId = (await Json(created)).GetProperty("id").GetGuid();
        await teacher.Client.PostAsJsonAsync("/api/students", new { phone = "0779990001", firstName = "Dash", lastName = "Student", classId, feePlan = "Full" });

        var stats = (await Json(await admin.GetAsync("/api/reports/dashboard"))).EnumerateArray().ToList();

        long Value(string key) => stats.Single(s => s.GetProperty("key").GetString() == key).GetProperty("value").GetInt64();
        Assert.True(Value("teachers") >= 1);
        Assert.True(Value("classes") >= 1);
        Assert.True(Value("students") >= 1);
        Assert.Contains(stats, s => s.GetProperty("key").GetString() == "institutes");

        var plans = stats.Where(s => s.GetProperty("key").GetString()!.StartsWith("plan:")).ToList();
        Assert.Contains(plans, p => p.GetProperty("label").GetString() == "Free");
        // Every active teacher is on exactly one plan.
        Assert.Equal(Value("teachers"), plans.Sum(p => p.GetProperty("value").GetInt64()));
    }

    [Fact]
    public async Task Only_the_Super_Admin_sees_the_dashboard_and_the_audit_log()
    {
        var teacher = await factory.NewTeacherAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/reports/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/reports/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/reports/audit/entities")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/reports/dashboard")).StatusCode);
    }

    [Fact]
    public async Task The_audit_log_filters_by_teacher_entity_and_action_and_pages_newest_first()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        var venueId = (await Json(await teacher.Client.PostAsJsonAsync("/api/classes/venues", new { name = "Main", district = "Kandy" }))).GetProperty("id").GetGuid();
        var made = await teacher.Client.PostAsJsonAsync("/api/classes/classes", new
        {
            title = "Audit", subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 100m, slots = new[] { new { day = 4, start = "16:00", end = "18:00" } },
        });
        var classId = (await Json(made)).GetProperty("id").GetGuid();
        for (var i = 0; i < 3; i++)
        {
            await teacher.Client.PostAsJsonAsync("/api/students", new { phone = $"07799900{10 + i}", firstName = "S" + i, lastName = "T", classId, feePlan = "Full" });
        }

        var all = await Json(await admin.GetAsync($"/api/reports/audit?teacherId={teacher.Id}&entity=Enrolment"));
        var items = all.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(teacher.Id, i.GetProperty("tenantId").GetGuid()));
        Assert.Equal(items.OrderByDescending(i => i.GetProperty("id").GetGuid()).Select(i => i.GetProperty("id").GetGuid()), items.Select(i => i.GetProperty("id").GetGuid()));

        var none = await Json(await admin.GetAsync($"/api/reports/audit?teacherId={other.Id}&entity=Enrolment"));
        Assert.Empty(none.GetProperty("items").EnumerateArray());

        Assert.Empty((await Json(await admin.GetAsync($"/api/reports/audit?teacherId={teacher.Id}&entity=Enrolment&action=deleted"))).GetProperty("items").EnumerateArray());
        Assert.Equal(3, (await Json(await admin.GetAsync($"/api/reports/audit?teacherId={teacher.Id}&entity=Enrolment&action=created"))).GetProperty("items").GetArrayLength());

        // The date range compares DateTimeOffset, which SQLite cannot do; it is checked against PostgreSQL by hand.

        var paged = await Json(await admin.GetAsync($"/api/reports/audit?teacherId={teacher.Id}&entity=Enrolment&skip=2"));
        Assert.Equal(1, paged.GetProperty("items").GetArrayLength());
        Assert.False(paged.GetProperty("hasMore").GetBoolean());

        var entities = (await Json(await admin.GetAsync("/api/reports/audit/entities"))).EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Enrolment", entities);
    }
}
