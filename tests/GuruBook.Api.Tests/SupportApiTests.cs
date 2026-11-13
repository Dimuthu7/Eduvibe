using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GuruBook.Api.Tests;

/// <summary>
/// Report an issue (Pro): the number to call, raising a ticket with a screenshot, the Super Admin's inbox and the reply that comes back,
/// and a teacher never seeing another teacher's tickets.
/// </summary>
public class SupportApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static byte[] Jpeg(string tail = "shot") => [0xFF, 0xD8, 0xFF, 0xE0, .. Encoding.UTF8.GetBytes(tail)];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private static ByteArrayContent Body(byte[] bytes, string type = "image/jpeg")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    /// <summary>A teacher on Pro, which is the plan that includes support.</summary>
    private async Task<TestTeacher> ProTeacher(string name)
    {
        var teacher = await factory.NewTeacherAsync(name);
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var pro = plans.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Pro").GetProperty("id").GetGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/subscription", new { planId = pro, note = "test" })).StatusCode);
        return teacher;
    }

    private static Task<HttpResponseMessage> Raise(HttpClient teacher, string details, string kind = "problem", string? screen = null) =>
        teacher.PostAsJsonAsync("/api/support/tickets", new { kind, details, screen });

    [Fact]
    public async Task Report_an_issue_is_a_paid_feature()
    {
        var free = await factory.NewTeacherAsync("Free Reporter");

        Assert.Equal("plan_feature_locked", await Code(await free.Client.GetAsync("/api/support")));
        var locked = await free.Client.PostAsJsonAsync("/api/support/tickets", new { kind = "problem", details = "Nothing loads" });
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Equal("priority_support", (await Json(locked)).GetProperty("feature").GetString());
    }

    [Fact]
    public async Task A_teacher_raises_a_ticket_and_support_writes_back()
    {
        var teacher = await ProTeacher("Ticket Writer");
        var admin = await factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/support/admin/settings", new { phone = "0712223344", hours = "9 AM to 6 PM" })).StatusCode);

        var screen = await Json(await teacher.Client.GetAsync("/api/support"));
        Assert.Equal("+94712223344", screen.GetProperty("phone").GetString());
        Assert.Equal("9 AM to 6 PM", screen.GetProperty("hours").GetString());
        Assert.Empty(screen.GetProperty("tickets").EnumerateArray());

        var raised = await Json(await Raise(teacher.Client, "Marks do not save for one student", screen: "/exams"));
        var ticketId = raised.GetProperty("id").GetGuid();
        Assert.Equal("Open", raised.GetProperty("status").GetString());
        Assert.Equal("problem", raised.GetProperty("kind").GetString());
        Assert.Equal("/exams", raised.GetProperty("screen").GetString());
        Assert.False(raised.GetProperty("hasShot").GetBoolean());
        Assert.Equal(JsonValueKind.Null, raised.GetProperty("answeredAt").ValueKind);

        // The Super Admin sees it with the teacher's name on it, because the inbox is every teacher's tickets.
        var inbox = await Json(await admin.GetAsync("/api/support/admin/tickets"));
        var mine = inbox.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == ticketId);
        Assert.Equal(teacher.Id, mine.GetProperty("teacherId").GetGuid());
        Assert.Contains("Ticket Writer", mine.GetProperty("teacherName").GetString());

        var answered = await Json(await admin.PostAsJsonAsync($"/api/support/admin/tickets/{ticketId}/replies", new { body = "Fixed in today's release." }));
        Assert.Equal("InProgress", answered.GetProperty("status").GetString());
        Assert.NotNull(answered.GetProperty("answeredAt").GetString());

        var after = await Json(await teacher.Client.GetAsync("/api/support"));
        var seen = after.GetProperty("tickets").EnumerateArray().Single();
        var reply = seen.GetProperty("replies").EnumerateArray().Single();
        Assert.Equal("Fixed in today's release.", reply.GetProperty("body").GetString());
        Assert.True(reply.GetProperty("fromSupport").GetBoolean());

        // The teacher is told in the app, so they do not have to keep the screen open.
        var notifications = await Json(await teacher.Client.GetAsync("/api/notifications"));
        var note = notifications.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "support_reply");
        Assert.Equal("/help", note.GetProperty("link").GetString());
        Assert.Equal("problem", note.GetProperty("params").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Writing_again_on_a_resolved_ticket_opens_it()
    {
        var teacher = await ProTeacher("Resolver");
        var admin = await factory.AdminClientAsync();
        var ticketId = (await Json(await Raise(teacher.Client, "The fee total looks wrong"))).GetProperty("id").GetGuid();

        var resolved = await Json(await admin.PutAsJsonAsync($"/api/support/admin/tickets/{ticketId}/status", new { status = "resolved" }));
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal("ticket_status_invalid", await Code(await admin.PutAsJsonAsync($"/api/support/admin/tickets/{ticketId}/status", new { status = "parked" })));

        var again = await Json(await teacher.Client.PostAsJsonAsync($"/api/support/tickets/{ticketId}/replies", new { body = "It is still wrong." }));
        Assert.Equal("Open", again.GetProperty("status").GetString());
        var written = again.GetProperty("replies").EnumerateArray().Single();
        Assert.Equal("It is still wrong.", written.GetProperty("body").GetString());
        Assert.False(written.GetProperty("fromSupport").GetBoolean());

        Assert.Equal("ticket_details_required", await Code(await teacher.Client.PostAsJsonAsync($"/api/support/tickets/{ticketId}/replies", new { body = "  " })));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await teacher.Client.PostAsJsonAsync($"/api/support/tickets/{Guid.CreateVersion7()}/replies", new { body = "Hello" })).StatusCode);

        // Only the statuses the app knows about, and only the ones the Super Admin may set.
        var open = await Json(await admin.GetAsync("/api/support/admin/tickets?status=open"));
        Assert.Contains(ticketId, open.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()));
        Assert.Equal("ticket_status_invalid", await Code(await admin.GetAsync("/api/support/admin/tickets?status=parked")));
    }

    [Fact]
    public async Task A_screenshot_is_sent_on_its_own_and_only_pictures_are_taken()
    {
        var teacher = await ProTeacher("Screenshot Sender");
        var admin = await factory.AdminClientAsync();
        var ticketId = (await Json(await Raise(teacher.Client, "This screen is empty"))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.GetAsync($"/api/support/tickets/{ticketId}/shot")).StatusCode);
        Assert.Equal("image_type_invalid", await Code(await teacher.Client.PutAsync($"/api/support/tickets/{ticketId}/shot", Body(Encoding.UTF8.GetBytes("not a picture")))));
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.PutAsync($"/api/support/tickets/{ticketId}/shot", Body(Jpeg()))).StatusCode);

        var shot = await teacher.Client.GetAsync($"/api/support/tickets/{ticketId}/shot");
        Assert.Equal("image/jpeg", shot.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", shot.Headers.GetValues("X-Content-Type-Options").Single());

        // The list says there is a picture but never carries it, so a long list stays small.
        var listed = (await Json(await teacher.Client.GetAsync("/api/support"))).GetProperty("tickets").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == ticketId);
        Assert.True(listed.GetProperty("hasShot").GetBoolean());
        Assert.Equal("image/jpeg", (await admin.GetAsync($"/api/support/admin/tickets/{ticketId}/shot")).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_teacher_sees_only_their_own_tickets()
    {
        var one = await ProTeacher("Support One");
        var two = await ProTeacher("Support Two");
        var ticketId = (await Json(await Raise(one.Client, "Only I should see this"))).GetProperty("id").GetGuid();

        Assert.DoesNotContain(
            ticketId,
            (await Json(await two.Client.GetAsync("/api/support"))).GetProperty("tickets").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await two.Client.PostAsJsonAsync($"/api/support/tickets/{ticketId}/replies", new { body = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await two.Client.GetAsync($"/api/support/tickets/{ticketId}/shot")).StatusCode);

        // The inbox and the number are the Super Admin's, not a teacher's.
        Assert.Equal(HttpStatusCode.Forbidden, (await one.Client.GetAsync("/api/support/admin/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await one.Client.PutAsJsonAsync("/api/support/admin/settings", new { phone = "0770000000" })).StatusCode);
    }

    [Fact]
    public async Task What_a_ticket_may_say_is_checked()
    {
        var teacher = await ProTeacher("Checker");

        Assert.Equal("ticket_kind_invalid", await Code(await Raise(teacher.Client, "Something", kind: "shouting")));
        Assert.Equal("ticket_details_required", await Code(await Raise(teacher.Client, "   ")));
        Assert.Equal("ticket_details_too_long", await Code(await Raise(teacher.Client, new string('x', 2001))));
        Assert.Equal(HttpStatusCode.OK, (await Raise(teacher.Client, "A fair question", kind: "question")).StatusCode);
    }

    [Fact]
    public async Task A_teacher_with_a_pile_of_open_tickets_cannot_raise_more()
    {
        var teacher = await ProTeacher("Piler");
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Raise(teacher.Client, $"Report {i}")).StatusCode);
        }

        var full = await Raise(teacher.Client, "One too many");
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.Equal("too_many_tickets", (await Json(full)).GetProperty("code").GetString());

        // Resolving one makes room again.
        var admin = await factory.AdminClientAsync();
        var oldest = (await Json(await teacher.Client.GetAsync("/api/support"))).GetProperty("tickets").EnumerateArray().Last().GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/support/admin/tickets/{oldest}/status", new { status = "Resolved" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Raise(teacher.Client, "Room at last")).StatusCode);
    }

    [Fact]
    public async Task The_super_admin_sets_the_number_teachers_are_shown()
    {
        var admin = await factory.AdminClientAsync();

        Assert.Equal("phone_invalid", await Code(await admin.PutAsJsonAsync("/api/support/admin/settings", new { phone = "12" })));
        Assert.Equal("support_hours_too_long", await Code(await admin.PutAsJsonAsync("/api/support/admin/settings", new { hours = new string('h', 101) })));

        var saved = await Json(await admin.PutAsJsonAsync("/api/support/admin/settings", new { phone = "0765554433", hours = "Weekdays" }));
        Assert.Equal("+94765554433", saved.GetProperty("phone").GetString());
        Assert.Equal("Weekdays", saved.GetProperty("hours").GetString());

        // Saving again replaces the one row rather than adding another.
        var cleared = await Json(await admin.PutAsJsonAsync("/api/support/admin/settings", new { phone = (string?)null, hours = (string?)null }));
        Assert.Null(cleared.GetProperty("phone").GetString());
        Assert.Null((await Json(await admin.GetAsync("/api/support/admin/settings"))).GetProperty("hours").GetString());
    }
}
