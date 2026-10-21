using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduVibe.Modules.WhatsApp;
using EduVibe.Modules.WhatsApp.Client;
using EduVibe.Modules.WhatsApp.Domain;
using EduVibe.Modules.WhatsApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Api.Tests;

/// <summary>WhatsApp (Pro): who may use it, what a preview and a send do, the monthly allowance, retries, Meta's webhook, STOP, and privacy.</summary>
public class WhatsAppApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 8000;

    private static string NewPhone() => $"076{Interlocked.Increment(ref _phones):D7}";

    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private async Task<TestTeacher> ProTeacher(string name = "Wanda")
    {
        var teacher = await factory.NewTeacherAsync(name);
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var pro = plans.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Pro").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/subscription", new { planId = pro, note = "test" })).StatusCode);
        factory.WhatsApp.Reset();
        return teacher;
    }

    private async Task<Guid> ClassOf(HttpClient teacher, string title = "WhatsApp class", bool daily = false)
    {
        var venueId = (await Json(await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Hall " + title, district = "Kandy" }))).GetProperty("id").GetGuid();
        var slots = daily ? Enumerable.Range(1, 7).Select(day => new { day, start = "16:00", end = "18:00" }).ToArray() : [new { day = 2, start = "16:00", end = "18:00" }];
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2000m, slots,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private sealed record Student(Guid StudentId, Guid EnrolmentId, string Phone);

    private static async Task<Student> Enrol(HttpClient teacher, Guid classId, string first, string? guardianPhone = null)
    {
        var phone = NewPhone();
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Silva", classId, feePlan = "Full", guardianPhone });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = (await Json(response)).GetProperty("row");
        return new Student(row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), phone);
    }

    private static string International(string local) => "+94" + local[1..];

    private async Task<int> Dispatch()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>().RunOnceAsync(CancellationToken.None);
    }

    private async Task<List<WhatsAppMessage>> Log(Guid teacherId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WhatsAppDbContext>();
        return (await db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == teacherId).ToListAsync()).OrderBy(m => m.Id).ToList();
    }

    private async Task Edit(Action<WhatsAppDbContext> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WhatsAppDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    private static string Reminders(Guid classId) => $"/api/fees/classes/{classId}/whatsapp";

    private HttpRequestMessage Webhook(string json, string? secret = ApiFactory.WhatsAppAppSecret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (secret is not null)
        {
            request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
        }

        return request;
    }

    [Fact]
    public async Task A_free_teacher_sees_whatsapp_locked_and_cannot_use_it()
    {
        var teacher = await factory.NewTeacherAsync("Freda");
        var classId = await ClassOf(teacher.Client);

        var status = await Json(await teacher.Client.GetAsync("/api/whatsapp/status"));
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, status.GetProperty("limit").GetInt32());

        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.GetAsync(Reminders(classId))));
        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.PostAsync(Reminders(classId), null)));
        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.GetAsync("/api/whatsapp/messages")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/whatsapp/status")).StatusCode);
    }

    [Fact]
    public async Task A_pro_teacher_previews_then_sends_fee_reminders_to_those_who_owe_and_never_twice_in_a_week()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client, "2027 A/L Maths");
        var owes = await Enrol(teacher.Client, classId, "Kamal", guardianPhone: "0711234567");
        var partly = await Enrol(teacher.Client, classId, "Nimali");
        var paid = await Enrol(teacher.Client, classId, "Paid");
        Assert.True((await teacher.Client.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = paid.EnrolmentId, amount = 2000m, method = "Cash" })).IsSuccessStatusCode);
        Assert.True((await teacher.Client.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = partly.EnrolmentId, amount = 500m, method = "Cash" })).IsSuccessStatusCode);

        var preview = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.True(preview.GetProperty("configured").GetBoolean());
        Assert.Equal(2, preview.GetProperty("total").GetInt32());
        Assert.Equal(2, preview.GetProperty("ready").GetInt32());
        Assert.Equal(500, preview.GetProperty("limit").GetInt32());
        Assert.Equal(0, preview.GetProperty("used").GetInt32());
        Assert.Empty(await Log(teacher.Id));

        var sent = await Json(await teacher.Client.PostAsync(Reminders(classId), null));
        Assert.Equal(2, sent.GetProperty("queued").GetInt32());
        Assert.Equal(2, await Dispatch());

        Assert.Equal(2, factory.WhatsApp.Sent.Count);
        var kamal = factory.WhatsApp.Sent.Single(s => s.Params[0] == "Kamal");
        Assert.Equal("94711234567", kamal.To);
        Assert.Equal("eduvibe_fee_reminder", kamal.Template);
        Assert.Equal("en", kamal.Language);
        Assert.Equal("2027 A/L Maths", kamal.Params[1]);
        Assert.Matches(@"^[A-Z][a-z]+ \d{4}$", kamal.Params[2]);
        Assert.Equal("LKR 2,000", kamal.Params[3]);
        Assert.Equal("Wanda Teacher", kamal.Params[4]);
        var nimali = factory.WhatsApp.Sent.Single(s => s.Params[0] == "Nimali");
        Assert.Equal("94" + partly.Phone[1..], nimali.To);
        Assert.Equal("LKR 1,500", nimali.Params[3]);

        var again = await Json(await teacher.Client.PostAsync(Reminders(classId), null));
        Assert.Equal(0, again.GetProperty("queued").GetInt32());
        Assert.Equal(2, again.GetProperty("alreadySent").GetInt32());
        Assert.Equal(0, await Dispatch());

        var list = await Json(await teacher.Client.GetAsync("/api/whatsapp/messages"));
        Assert.Equal(2, list.GetProperty("status").GetProperty("used").GetInt32());
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("Sent", i.GetProperty("status").GetString()));
        Assert.All(items, i => Assert.StartsWith("•••• ", i.GetProperty("phone").GetString()));
        Assert.DoesNotContain(owes.Phone[1..], await (await teacher.Client.GetAsync("/api/whatsapp/messages")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Another_teacher_never_sees_these_messages_or_blocks_them()
    {
        var teacher = await ProTeacher("First");
        var other = await ProTeacher("Second");
        var classId = await ClassOf(teacher.Client);
        await Enrol(teacher.Client, classId, "Mine");
        await teacher.Client.PostAsync(Reminders(classId), null);

        var theirs = await Json(await other.Client.GetAsync("/api/whatsapp/messages"));
        Assert.Empty(theirs.GetProperty("items").EnumerateArray());
        Assert.Equal(0, theirs.GetProperty("status").GetProperty("used").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(Reminders(classId))).StatusCode);
    }

    [Fact]
    public async Task Sending_stops_at_the_monthly_allowance_and_the_preview_says_so()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client);
        for (var i = 0; i < 3; i++)
        {
            await Enrol(teacher.Client, classId, "Student" + (char)('A' + i));
        }

        // The Pro plan allows 500 a month: use 499 of them.
        var month = Today.ToString("yyyy-MM");
        await Edit(db =>
        {
            for (var i = 0; i < 499; i++)
            {
                db.Messages.Add(new WhatsAppMessage
                {
                    TenantId = teacher.Id, StudentId = Guid.NewGuid(), StudentName = "Earlier", Kind = "fee_reminder", ToPhone = "+94710000000",
                    ParamsJson = "[]", DedupKey = "earlier:" + i, Month = month, Status = MessageStatus.Sent,
                });
            }
        });

        var preview = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.Equal(1, preview.GetProperty("ready").GetInt32());
        Assert.Equal(2, preview.GetProperty("overAllowance").GetInt32());
        Assert.Equal(499, preview.GetProperty("used").GetInt32());

        var sent = await Json(await teacher.Client.PostAsync(Reminders(classId), null));
        Assert.Equal(1, sent.GetProperty("queued").GetInt32());
        Assert.Equal(2, sent.GetProperty("overAllowance").GetInt32());

        var status = await Json(await teacher.Client.GetAsync("/api/whatsapp/status"));
        Assert.Equal(0, status.GetProperty("remaining").GetInt32());

        // Last month's messages do not count: the allowance starts again each month.
        await Edit(db => { foreach (var m in db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == teacher.Id && m.DedupKey.StartsWith("earlier:"))) { m.Month = "2000-01"; } });
        Assert.Equal(499, (await Json(await teacher.Client.GetAsync("/api/whatsapp/status"))).GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task Nothing_is_queued_while_whatsapp_is_not_set_up()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client);
        await Enrol(teacher.Client, classId, "Waiting");
        factory.WhatsApp.IsConfigured = false;
        try
        {
            Assert.False((await Json(await teacher.Client.GetAsync("/api/whatsapp/status"))).GetProperty("configured").GetBoolean());
            Assert.False((await Json(await teacher.Client.GetAsync(Reminders(classId)))).GetProperty("configured").GetBoolean());
            var sent = await teacher.Client.PostAsync(Reminders(classId), null);
            Assert.Equal(HttpStatusCode.Conflict, sent.StatusCode);
            Assert.Equal("whatsapp_not_configured", await Code(sent));
            Assert.Empty(await Log(teacher.Id));
        }
        finally
        {
            factory.WhatsApp.Reset();
        }
    }

    [Fact]
    public async Task A_message_whatsapp_refuses_is_marked_failed_and_does_not_use_the_allowance()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client);
        await Enrol(teacher.Client, classId, "Nowhatsapp");
        factory.WhatsApp.Outcome = new SendOutcome.Rejected("not_on_whatsapp");

        await teacher.Client.PostAsync(Reminders(classId), null);
        Assert.Equal(1, await Dispatch());

        var message = Assert.Single(await Log(teacher.Id));
        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.Equal("not_on_whatsapp", message.Error);
        Assert.Equal(0, (await Json(await teacher.Client.GetAsync("/api/whatsapp/status"))).GetProperty("used").GetInt32());

        // A failed message may be tried again by sending again.
        factory.WhatsApp.Outcome = null;
        var again = await Json(await teacher.Client.PostAsync(Reminders(classId), null));
        Assert.Equal(1, again.GetProperty("queued").GetInt32());
    }

    [Fact]
    public async Task A_busy_whatsapp_is_tried_again_later_and_given_up_on_after_four_tries()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client);
        await Enrol(teacher.Client, classId, "Busy");
        factory.WhatsApp.Outcome = new SendOutcome.Retry("busy");

        await teacher.Client.PostAsync(Reminders(classId), null);
        Assert.Equal(1, await Dispatch());
        var message = Assert.Single(await Log(teacher.Id));
        Assert.Equal(MessageStatus.Queued, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.NotNull(message.NextAttemptAt);

        // Not due yet, so nothing is tried.
        Assert.Equal(0, await Dispatch());

        for (var round = 2; round <= WhatsAppDispatcher.MaxAttempts; round++)
        {
            await Edit(db => { foreach (var m in db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == teacher.Id)) { m.NextAttemptAt = null; } });
            Assert.Equal(1, await Dispatch());
        }

        var last = Assert.Single(await Log(teacher.Id));
        Assert.Equal(MessageStatus.Failed, last.Status);
        Assert.Equal("unreachable", last.Error);
        Assert.Equal(WhatsAppDispatcher.MaxAttempts, last.Attempts);

        // Once it works again the same message can be queued afresh, then goes through.
        factory.WhatsApp.Outcome = null;
        await teacher.Client.PostAsync(Reminders(classId), null);
        Assert.Equal(1, await Dispatch());
        Assert.Single(factory.WhatsApp.Sent);
    }

    [Fact]
    public async Task Absence_alerts_go_to_the_parents_of_absent_students_once_after_attendance_is_saved()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client, "Daily maths", daily: true);
        var absent = await Enrol(teacher.Client, classId, "Absentee", guardianPhone: "0772223333");
        var late = await Enrol(teacher.Client, classId, "Latecomer");
        await Enrol(teacher.Client, classId, "Present");

        var sessions = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions?date={Today:yyyy-MM-dd}"));
        var sessionId = sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
        var url = $"/api/attendance/sessions/{sessionId}/whatsapp";

        Assert.Equal("attendance_not_taken", await Code(await teacher.Client.GetAsync(url)));

        var saved = await teacher.Client.PutAsJsonAsync($"/api/attendance/sessions/{sessionId}/marks", new
        {
            marks = new[] { new { enrolmentId = absent.EnrolmentId, status = "Absent" }, new { enrolmentId = late.EnrolmentId, status = "Late" } },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var preview = await Json(await teacher.Client.GetAsync(url));
        Assert.Equal(1, preview.GetProperty("total").GetInt32());
        Assert.Equal(1, preview.GetProperty("ready").GetInt32());

        Assert.Equal(1, (await Json(await teacher.Client.PostAsync(url, null))).GetProperty("queued").GetInt32());
        await Dispatch();
        var sent = Assert.Single(factory.WhatsApp.Sent);
        Assert.Equal("94772223333", sent.To);
        Assert.Equal("eduvibe_absence_alert", sent.Template);
        Assert.Equal(["Absentee", "Daily maths", Today.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture), "Wanda Teacher"], sent.Params);

        var again = await Json(await teacher.Client.PostAsync(url, null));
        Assert.Equal(0, again.GetProperty("queued").GetInt32());
        Assert.Equal(1, again.GetProperty("alreadySent").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await teacher.Client.GetAsync($"/api/attendance/sessions/{Guid.NewGuid()}/whatsapp")).StatusCode);
    }

    [Fact]
    public async Task Results_can_be_sent_only_after_publishing_and_only_to_students_with_a_result()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client, "Science class");
        var first = await Enrol(teacher.Client, classId, "Topper");
        var second = await Enrol(teacher.Client, classId, "Second");
        var absent = await Enrol(teacher.Client, classId, "Away");
        await Enrol(teacher.Client, classId, "Unmarked");

        var exam = await Json(await teacher.Client.PostAsJsonAsync("/api/exams", new
        {
            classId, name = "Term test", heldOn = Today, parts = new[] { new { name = "MCQ", maxMark = 40m }, new { name = "Essay", maxMark = 60m } },
        }));
        var examId = exam.GetProperty("id").GetGuid();
        var parts = exam.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToArray();
        object Row(Guid enrolment, decimal mcq, decimal essay, bool away = false) =>
            new { enrolmentId = enrolment, absent = away, marks = away ? new Dictionary<Guid, decimal?>() : new Dictionary<Guid, decimal?> { [parts[0]] = mcq, [parts[1]] = essay } };
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}/marks", new
        {
            rows = new[] { Row(first.EnrolmentId, 40, 50), Row(second.EnrolmentId, 20, 30.5m), Row(absent.EnrolmentId, 0, 0, away: true) },
        })).StatusCode);

        var url = $"/api/exams/{examId}/whatsapp";
        Assert.Equal("exam_not_published", await Code(await teacher.Client.GetAsync(url)));
        Assert.Equal("exam_not_published", await Code(await teacher.Client.PostAsync(url, null)));

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync($"/api/exams/{examId}/publish", null)).StatusCode);
        var preview = await Json(await teacher.Client.GetAsync(url));
        Assert.Equal(2, preview.GetProperty("total").GetInt32());

        Assert.Equal(2, (await Json(await teacher.Client.PostAsync(url, null))).GetProperty("queued").GetInt32());
        await Dispatch();
        var top = factory.WhatsApp.Sent.Single(s => s.Params[0] == "Topper");
        Assert.Equal("eduvibe_results", top.Template);
        Assert.Equal(["Topper", "Term test", "Science class", "90 / 100", "A", "1 of 2", "Wanda Teacher"], top.Params);
        var low = factory.WhatsApp.Sent.Single(s => s.Params[0] == "Second");
        Assert.Equal("50.5 / 100", low.Params[3]);
        Assert.Equal("2 of 2", low.Params[5]);

        Assert.Equal(2, (await Json(await teacher.Client.PostAsync(url, null))).GetProperty("alreadySent").GetInt32());
    }

    [Fact]
    public async Task Meta_confirms_the_webhook_address_only_with_the_right_token()
    {
        var anon = factory.CreateClient();
        var ok = await anon.GetAsync($"/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token={ApiFactory.WhatsAppVerifyToken}&hub.challenge=12345");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("12345", await ok.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await anon.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anon.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.challenge=1")).StatusCode);
    }

    [Fact]
    public async Task Delivery_reports_update_the_message_and_calls_without_a_valid_signature_change_nothing()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client);
        await Enrol(teacher.Client, classId, "Reported");
        await teacher.Client.PostAsync(Reminders(classId), null);
        await Dispatch();
        var id = Assert.Single(await Log(teacher.Id)).ProviderMessageId!;
        var anon = factory.CreateClient();

        string Report(string status) =>
            """{"entry":[{"changes":[{"value":{"statuses":[{"id":"ID","status":"STATUS"}]}}]}]}""".Replace("ID", id).Replace("STATUS", status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Webhook(Report("delivered"), secret: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Webhook(Report("delivered"), secret: "wrong-secret"))).StatusCode);
        Assert.Equal(MessageStatus.Sent, Assert.Single(await Log(teacher.Id)).Status);

        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook(Report("delivered")))).StatusCode);
        Assert.Equal(MessageStatus.Delivered, Assert.Single(await Log(teacher.Id)).Status);
        await anon.SendAsync(Webhook(Report("read")));
        Assert.Equal(MessageStatus.Read, Assert.Single(await Log(teacher.Id)).Status);

        // Reports can arrive out of order; a later "delivered" never takes a message back from read.
        await anon.SendAsync(Webhook(Report("delivered")));
        Assert.Equal(MessageStatus.Read, Assert.Single(await Log(teacher.Id)).Status);

        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook("""{"entry":[{"changes":[{"value":{"statuses":[{"id":"unknown","status":"read"}]}}]}]}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.SendAsync(Webhook("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.SendAsync(Webhook("not json"))).StatusCode);
    }

    [Fact]
    public async Task A_failed_delivery_report_says_why()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client);
        await Enrol(teacher.Client, classId, "Undeliverable");
        await teacher.Client.PostAsync(Reminders(classId), null);
        await Dispatch();
        var id = Assert.Single(await Log(teacher.Id)).ProviderMessageId!;

        var body = """{"entry":[{"changes":[{"value":{"statuses":[{"id":"ID","status":"failed","errors":[{"code":131026,"title":"Message undeliverable"}]}]}}]}]}""".Replace("ID", id);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().SendAsync(Webhook(body))).StatusCode);

        var message = Assert.Single(await Log(teacher.Id));
        Assert.Equal(MessageStatus.Failed, message.Status);
        Assert.Equal("not_on_whatsapp", message.Error);
        Assert.Equal(0, (await Json(await teacher.Client.GetAsync("/api/whatsapp/status"))).GetProperty("used").GetInt32());
    }

    [Fact]
    public async Task A_parent_who_replies_STOP_is_not_messaged_again_until_they_reply_START()
    {
        var teacher = await ProTeacher();
        var classId = await ClassOf(teacher.Client);
        var student = await Enrol(teacher.Client, classId, "Optout");
        await teacher.Client.PostAsync(Reminders(classId), null);
        await Dispatch();
        var digits = "94" + student.Phone[1..];
        var anon = factory.CreateClient();

        string Reply(string text) =>
            """{"entry":[{"changes":[{"value":{"messages":[{"from":"DIGITS","text":{"body":"TEXT"}}]}}]}]}""".Replace("DIGITS", digits).Replace("TEXT", text);

        await anon.SendAsync(Webhook(Reply("Stop.")));
        // The reminder was sent this week already, so clear it to see the opt-out on its own.
        await Edit(db => { foreach (var m in db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == teacher.Id)) { m.DedupKey = "old"; } });

        var preview = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.Equal(1, preview.GetProperty("optedOut").GetInt32());
        Assert.Equal(0, preview.GetProperty("ready").GetInt32());
        Assert.Equal(0, (await Json(await teacher.Client.PostAsync(Reminders(classId), null))).GetProperty("queued").GetInt32());

        await anon.SendAsync(Webhook(Reply("hello")));
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync(Reminders(classId)))).GetProperty("optedOut").GetInt32());

        await anon.SendAsync(Webhook(Reply("START")));
        var after = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.Equal(0, after.GetProperty("optedOut").GetInt32());
        Assert.Equal(1, after.GetProperty("ready").GetInt32());
    }

    [Fact]
    public async Task The_message_log_is_in_a_teacher_export_and_goes_when_the_student_is_erased()
    {
        var teacher = await ProTeacher("Loggy");
        var admin = await factory.AdminClientAsync();
        var classId = await ClassOf(teacher.Client);
        var student = await Enrol(teacher.Client, classId, "Erasable");
        await teacher.Client.PostAsync(Reminders(classId), null);

        var export = await (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).Content.ReadAsStringAsync();
        Assert.Contains("whatsapp", export);
        Assert.Contains("Erasable Silva", export);

        var studentExport = await (await admin.GetAsync($"/api/privacy/students/{student.StudentId}/export")).Content.ReadAsStringAsync();
        Assert.Contains("fee_reminder", studentExport);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/privacy/students/{student.StudentId}/erase", new { confirm = "erasable silva" })).StatusCode);
        Assert.Empty(await Log(teacher.Id));
    }
}
