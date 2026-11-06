using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GuruBook.Modules.Sms;
using GuruBook.Modules.Sms.Client;
using GuruBook.Modules.Sms.Domain;
using GuruBook.Modules.Sms.Services;
using GuruBook.Modules.WhatsApp;
using GuruBook.Modules.WhatsApp.Client;
using GuruBook.Modules.WhatsApp.Services;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Api.Tests;

/// <summary>
/// SMS (Pro): who may use it, the channel choice, the words that go out, the allowance in paid parts, WhatsApp handing a message over,
/// and a parent asking for no more texts.
/// </summary>
public class SmsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 9000;

    private static string NewPhone() => $"071{Interlocked.Increment(ref _phones):D7}";

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private async Task<TestTeacher> ProTeacher(string name = "Sumith")
    {
        var teacher = await factory.NewTeacherAsync(name);
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var pro = plans.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Pro").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/subscription", new { planId = pro, note = "test" })).StatusCode);
        factory.Sms.Reset();
        factory.WhatsApp.Reset();
        return teacher;
    }

    private async Task<Guid> ClassOf(HttpClient teacher, string title)
    {
        var venueId = (await Json(await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Hall " + title, district = "Kandy" }))).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2000m, slots = new[] { new { day = 2, start = "16:00", end = "18:00" } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> Enrol(HttpClient teacher, Guid classId, string first, string? guardianPhone = null)
    {
        var response = await teacher.PostAsJsonAsync(
            "/api/students", new { phone = NewPhone(), firstName = first, lastName = "Silva", classId, feePlan = "Full", guardianPhone });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("row").GetProperty("studentId").GetGuid();
    }

    private static string Reminders(Guid classId) => $"/api/fees/classes/{classId}/whatsapp";

    /// <summary>A student with their own sign-in, to check that someone who is not a teacher can still stop texts to their number.</summary>
    private async Task<HttpClient> StudentClient(HttpClient teacher, Guid classId)
    {
        var phone = NewPhone();
        var created = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = "Hiru", lastName = "Silva", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var otp = (await Json(created)).GetProperty("oneTimePassword").GetString()!;

        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync(
            "/api/identity/change-password",
            new { currentPassword = otp, newPassword = "Student-pass-1", username = "s" + Guid.NewGuid().ToString("N")[..10] });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);
        return client;
    }

    private async Task<List<SmsMessage>> Log(Guid teacherId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        return (await db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == teacherId).ToListAsync()).OrderBy(m => m.Id).ToList();
    }

    /// <summary>
    /// Sends the whole queue. It belongs to no teacher, so it also picks up anything an earlier test left waiting: a test checks its own
    /// log rather than how many texts went out.
    /// </summary>
    private async Task Dispatch()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SmsDispatcher>().RunOnceAsync(CancellationToken.None);
    }

    private async Task DispatchWhatsApp()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>().RunOnceAsync(CancellationToken.None);
    }

    private async Task Edit(Action<SmsDbContext> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_free_teacher_sees_sms_locked_and_cannot_set_it_up()
    {
        var teacher = await factory.NewTeacherAsync("Fiona");

        var status = await Json(await teacher.Client.GetAsync("/api/sms/status"));
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, status.GetProperty("limit").GetInt32());
        Assert.Equal(MessageChannels.WhatsAppThenSms, status.GetProperty("channel").GetString());

        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.GetAsync("/api/sms/messages")));
        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.PutAsJsonAsync("/api/sms/channel", new { channel = MessageChannels.SmsOnly })));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/sms/status")).StatusCode);
    }

    [Fact]
    public async Task A_pro_teacher_who_chooses_sms_sends_texts_instead_of_whatsapp_and_the_words_name_the_fee()
    {
        var teacher = await ProTeacher("Sunethra");
        var classId = await ClassOf(teacher.Client, "SMS fees");
        await Enrol(teacher.Client, classId, "Kamal", NewPhone());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/sms/channel", new { channel = MessageChannels.SmsOnly })).StatusCode);

        var preview = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.Equal(MessageChannels.SmsOnly, preview.GetProperty("channel").GetString());
        Assert.Equal(1, preview.GetProperty("ready").GetInt32());

        var sent = await Json(await teacher.Client.PostAsync(Reminders(classId), null));
        Assert.Equal(1, sent.GetProperty("queued").GetInt32());
        Assert.Empty(factory.WhatsApp.Sent);

        await Dispatch();
        var row = Assert.Single(await Log(teacher.Id));
        Assert.Contains("Kamal", row.Text);
        Assert.Contains("SMS fees", row.Text);
        Assert.Contains("LKR", row.Text);
        Assert.Contains("To stop: ask your teacher.", row.Text);
        Assert.Contains(factory.Sms.Sent, sent => sent.Text == row.Text);

        Assert.Equal(SmsStatus.Sent, row.Status);
        Assert.NotNull(row.ProviderMessageId);
        Assert.Equal(1, row.Parts);
        Assert.Equal("en", row.Language);
        Assert.False(row.Fallback);

        var list = await Json(await teacher.Client.GetAsync("/api/sms/messages"));
        var item = Assert.Single(list.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("Sent", item.GetProperty("status").GetString());
        Assert.StartsWith("••••", item.GetProperty("phone").GetString());
        Assert.Equal(1, list.GetProperty("status").GetProperty("used").GetInt32());
        Assert.Equal(300, list.GetProperty("status").GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task Whatsapp_hands_a_parent_who_is_not_on_whatsapp_over_to_sms()
    {
        var teacher = await ProTeacher("Hema");
        var classId = await ClassOf(teacher.Client, "Handover class");
        await Enrol(teacher.Client, classId, "Nimal", NewPhone());

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync(Reminders(classId), null)).StatusCode);
        factory.WhatsApp.Outcome = new SendOutcome.Rejected("not_on_whatsapp");
        await DispatchWhatsApp();

        var text = Assert.Single(await Log(teacher.Id));
        Assert.True(text.Fallback);
        Assert.Equal(SmsStatus.Queued, text.Status);
        Assert.Contains("Nimal", text.Text);

        var messages = await Json(await teacher.Client.GetAsync("/api/whatsapp/messages"));
        var item = Assert.Single(messages.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("Failed", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("handedToSms").GetBoolean());

        // A teacher who chose SMS alone never had a WhatsApp message to hand over, and one refusal that is not about WhatsApp is not handed over either.
        factory.WhatsApp.Outcome = new SendOutcome.Rejected("template_problem");
        var other = await ProTeacher("Hemantha");
        var otherClass = await ClassOf(other.Client, "No handover");
        await Enrol(other.Client, otherClass, "Sanka", NewPhone());
        Assert.Equal(HttpStatusCode.OK, (await other.Client.PostAsync(Reminders(otherClass), null)).StatusCode);
        await DispatchWhatsApp();
        Assert.Empty(await Log(other.Id));
    }

    [Fact]
    public async Task A_text_is_written_in_the_teachers_own_language_and_sinhala_costs_more_parts()
    {
        var teacher = await ProTeacher("Saman");
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/identity/me/language", new { language = "si" })).StatusCode);
        var classId = await ClassOf(teacher.Client, "Sinhala class");
        await Enrol(teacher.Client, classId, "Amara", NewPhone());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/sms/channel", new { channel = MessageChannels.SmsOnly })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync(Reminders(classId), null)).StatusCode);
        var row = Assert.Single(await Log(teacher.Id));
        Assert.Equal("si", row.Language);
        Assert.Contains("ගාස්තුව", row.Text);
        // Sinhala is sent as Unicode, which fits only 70 letters in a part, so this one is paid for more than once.
        Assert.True(row.Parts > 1, $"expected more than one part, got {row.Parts} for {row.Text.Length} letters");
    }

    [Fact]
    public async Task The_allowance_counts_parts_and_stops_the_rest()
    {
        var teacher = await ProTeacher("Kalani");
        var classId = await ClassOf(teacher.Client, "Allowance class");
        await Enrol(teacher.Client, classId, "Ravi", NewPhone());
        await Enrol(teacher.Client, classId, "Tharu", NewPhone());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/sms/channel", new { channel = MessageChannels.SmsOnly })).StatusCode);

        // Pro allows 300 parts a month. Earlier texts have used 299 of them, so there is room for one more and not two.
        var month = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime).ToString("yyyy-MM");
        await Edit(db => db.Messages.Add(new SmsMessage
        {
            TenantId = teacher.Id, StudentId = Guid.NewGuid(), StudentName = "Earlier", Kind = WhatsAppKinds.FeeReminder,
            ToPhone = "+94710000000", Text = "earlier", Language = "en", Parts = 299, DedupKey = "earlier:0", Month = month,
            Status = SmsStatus.Sent,
        }));

        var preview = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.Equal(299, preview.GetProperty("used").GetInt32());
        Assert.Equal(1, preview.GetProperty("ready").GetInt32());
        Assert.Equal(1, preview.GetProperty("overAllowance").GetInt32());

        var sent = await Json(await teacher.Client.PostAsync(Reminders(classId), null));
        Assert.Equal(1, sent.GetProperty("queued").GetInt32());
        Assert.Equal(1, sent.GetProperty("overAllowance").GetInt32());
        Assert.Equal(2, (await Log(teacher.Id)).Count);

        var status = await Json(await teacher.Client.GetAsync("/api/sms/status"));
        Assert.Equal(0, status.GetProperty("remaining").GetInt32());

        // Last month's texts do not count: the allowance starts again each month.
        await Edit(db =>
        {
            foreach (var m in db.Messages.IgnoreQueryFilters().Where(m => m.TenantId == teacher.Id && m.DedupKey.StartsWith("earlier:")))
            {
                m.Month = "2000-01";
            }
        });
        Assert.Equal(299, (await Json(await teacher.Client.GetAsync("/api/sms/status"))).GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task A_parent_can_stop_texts_themselves_and_a_teacher_can_stop_them_for_a_number()
    {
        var teacher = await ProTeacher("Niluka");
        var classId = await ClassOf(teacher.Client, "Stop class");
        var guardian = NewPhone();
        await Enrol(teacher.Client, classId, "Dinuka", guardian);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/sms/channel", new { channel = MessageChannels.SmsOnly })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync(Reminders(classId), null)).StatusCode);

        var id = Assert.Single(await Log(teacher.Id)).Id;
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync($"/api/sms/messages/{id}/stop", new { on = false })).StatusCode);

        var list = await Json(await teacher.Client.GetAsync("/api/sms/messages"));
        Assert.True(Assert.Single(list.GetProperty("items").EnumerateArray().ToList()).GetProperty("stopped").GetBoolean());

        // Nothing more goes to that number, even about another month's fee.
        var preview = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.Equal(0, preview.GetProperty("ready").GetInt32());
        Assert.Equal(1, preview.GetProperty("optedOut").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync($"/api/sms/messages/{id}/stop", new { on = true })).StatusCode);
        var again = await Json(await teacher.Client.GetAsync(Reminders(classId)));
        Assert.Equal(0, again.GetProperty("optedOut").GetInt32());
        // Not stopped any more, but this month's reminder has already gone to them.
        Assert.Equal(1, again.GetProperty("alreadySent").GetInt32());

        // And a student can switch their own number off, without being a teacher.
        var student = await StudentClient(teacher.Client, classId);
        Assert.True((await Json(await student.GetAsync("/api/sms/my-texts"))).GetProperty("on").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await student.PutAsJsonAsync("/api/sms/my-texts", new { on = false })).StatusCode);
        Assert.False((await Json(await student.GetAsync("/api/sms/my-texts"))).GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task A_refused_text_is_never_tried_again_and_a_busy_provider_is()
    {
        var teacher = await ProTeacher("Chamara");
        var classId = await ClassOf(teacher.Client, "Retry class");
        await Enrol(teacher.Client, classId, "Isuru", NewPhone());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/sms/channel", new { channel = MessageChannels.SmsOnly })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync(Reminders(classId), null)).StatusCode);

        factory.Sms.Outcome = new SmsOutcome.Retry("busy");
        await Dispatch();
        var waiting = Assert.Single(await Log(teacher.Id));
        Assert.Equal(SmsStatus.Queued, waiting.Status);
        Assert.Equal(1, waiting.Attempts);
        Assert.NotNull(waiting.NextAttemptAt);

        await Edit(db =>
        {
            var row = db.Messages.IgnoreQueryFilters().Single(m => m.Id == waiting.Id);
            row.NextAttemptAt = null;
        });
        factory.Sms.Outcome = new SmsOutcome.Rejected("no_credit");
        await Dispatch();
        var failed = Assert.Single(await Log(teacher.Id));
        Assert.Equal(SmsStatus.Failed, failed.Status);
        Assert.Equal("no_credit", failed.Error);
        Assert.Null(failed.NextAttemptAt);

        factory.Sms.Outcome = null;
        await Dispatch();
        Assert.DoesNotContain(factory.Sms.Sent, sent => sent.Text == failed.Text);

        // A failed text is not paid for, so it leaves the allowance alone and the same reminder may be tried again.
        Assert.Equal(0, (await Json(await teacher.Client.GetAsync("/api/sms/status"))).GetProperty("used").GetInt32());
        Assert.Equal(1, (await Json(await teacher.Client.GetAsync(Reminders(classId)))).GetProperty("ready").GetInt32());
    }

    [Fact]
    public void Parts_are_counted_the_way_a_phone_counts_them()
    {
        Assert.Equal(1, MessageTexts.Parts(new string('a', 160)));
        Assert.Equal(2, MessageTexts.Parts(new string('a', 161)));
        Assert.Equal(1, MessageTexts.Parts(new string('ක', 70)));
        Assert.Equal(2, MessageTexts.Parts(new string('ක', 71)));
        Assert.Equal(1, MessageTexts.Parts(""));
    }

    [Fact]
    public void Every_event_has_words_in_every_language()
    {
        foreach (var name in MessageEvents.All)
        {
            foreach (var language in MessageTexts.Languages)
            {
                var values = MessageEvents.Of(name).Fields.Select((_, n) => $"v{n + 1}").ToList();
                var text = MessageTexts.Compose(name, language, values, withStopNote: false);
                Assert.All(values, value => Assert.Contains(value, text));
                Assert.DoesNotContain("{", text);
            }
        }
    }

    [Fact]
    public void A_provider_refusal_is_turned_into_words_the_app_knows()
    {
        Assert.Equal("auth_problem", TextLkSmsClient.Describe(HttpStatusCode.Unauthorized, "{}"));
        Assert.Equal("no_credit", TextLkSmsClient.Describe(HttpStatusCode.BadRequest, "{\"message\":\"Insufficient balance\"}"));
        Assert.Equal("bad_number", TextLkSmsClient.Describe(HttpStatusCode.BadRequest, "{\"message\":\"The recipient is invalid\"}"));
        Assert.Equal("sender_problem", TextLkSmsClient.Describe(HttpStatusCode.BadRequest, "{\"message\":\"sender_id not approved\"}"));
        Assert.Equal("failed", TextLkSmsClient.Describe(HttpStatusCode.BadRequest, "{\"message\":\"something else\"}"));
    }
}
