using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GuruBook.Modules.Fees;
using GuruBook.Modules.Fees.Services;
using GuruBook.Modules.Sms.Services;
using GuruBook.Modules.WhatsApp.Services;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Api.Tests;

/// <summary>
/// Message settings (Pro): which alerts go out, how each one is sent, the teacher's own wording and their line at the end of a WhatsApp
/// message, and telling parents when a child is missing classes.
/// </summary>
public class MessagingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 6000;

    private static string NewPhone() => $"077{Interlocked.Increment(ref _phones):D7}";

    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private HttpClient Bearer(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<TestTeacher> ProTeacher(string name)
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

    /// <summary>A class that meets every day, so today always has a session, with a fee due on the 10th.</summary>
    private async Task<Guid> DailyClass(HttpClient teacher, string title)
    {
        var venueId = (await Json(await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Hall " + title, district = "Kandy" }))).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2000m, slots = Enumerable.Range(1, 7).Select(day => new { day, start = "16:00", end = "18:00" }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private sealed record Child(Guid StudentId, Guid EnrolmentId, string FullName);

    private static async Task<Child> Enrol(HttpClient teacher, Guid classId, string first, string? guardianPhone = null)
    {
        var response = await teacher.PostAsJsonAsync(
            "/api/students", new { phone = NewPhone(), firstName = first, lastName = "Perera", classId, feePlan = "Full", guardianPhone });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = (await Json(response)).GetProperty("row");
        return new Child(row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), row.GetProperty("fullName").GetString()!);
    }

    private async Task<HttpClient> ParentOf(HttpClient teacher, Child child, string guardianPhone)
    {
        var invite = await teacher.PostAsync($"/api/students/{child.StudentId}/parent", null);
        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);
        var otp = (await Json(invite)).GetProperty("oneTimePassword").GetString()!;
        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = guardianPhone, password = otp });
        var setup = Bearer((await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync(
            "/api/identity/change-password",
            new { currentPassword = otp, newPassword = "Parent-pass-1", username = "p" + Guid.NewGuid().ToString("N")[..10] });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        return Bearer((await Json(changed)).GetProperty("accessToken").GetString()!);
    }

    private static async Task<JsonElement[]> Of(HttpClient person, string kind) =>
        [.. (await Json(await person.GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("kind").GetString() == kind)];

    private static async Task<Guid> TodaySession(HttpClient teacher, Guid classId)
    {
        var sessions = await Json(await teacher.GetAsync($"/api/attendance/sessions?date={Today:yyyy-MM-dd}"));
        return sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SaveMarks(HttpClient teacher, Guid sessionId, params object[] marks) =>
        teacher.PutAsJsonAsync($"/api/attendance/sessions/{sessionId}/marks", new { marks });

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static JsonElement Event(JsonElement settings, string name) =>
        settings.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("event").GetString() == name);

    private async Task<JsonElement> Settings(HttpClient teacher) => await Json(await teacher.GetAsync("/api/messaging"));

    private static Task<HttpResponseMessage> Save(HttpClient teacher, string name, object body) =>
        teacher.PutAsJsonAsync($"/api/messaging/{name}", body);

    /// <summary>
    /// Runs the hourly fee job at a moment when a reminder is actually due: a weekday morning, after the 10th, when a fee for this month has
    /// passed its due date, so the "due" or a weekly step is the one that goes out.
    /// </summary>
    private async Task RunFeeJob()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FeesDbContext>();

        // This month's fees are made first, then the job is run the morning after the earliest one fell due, which is when the "due" step goes out.
        var now = new DateTimeOffset(Today.Year, Today.Month, Today.Day, 9, 0, 0, TimeSpan.FromMinutes(330));
        var dues = new FeeDueService(db, sp.GetRequiredService<IClassRoster>(), sp.GetRequiredService<IClassDirectory>(), new FixedClock(now));
        await dues.EnsureForAllTeachersAsync([now.ToString("yyyy-MM")], CancellationToken.None);

        var due = (await db.Dues.IgnoreQueryFilters().Select(d => d.DueDate).ToListAsync()).DefaultIfEmpty(Today).Min();
        var day = due.AddDays(1);
        var at = new DateTimeOffset(day.Year, day.Month, day.Day, 9, 0, 0, TimeSpan.FromMinutes(330));
        var clock = new FixedClock(at);
        await new FeeReminderService(
            sp.GetRequiredService<FeesDbContext>(), sp.GetRequiredService<IClassRoster>(), sp.GetRequiredService<IClassDirectory>(),
            sp.GetRequiredService<INotifier>(), sp.GetRequiredService<IMessageRouter>(), sp.GetRequiredService<IStudentContacts>(),
            sp.GetRequiredService<ITeacherDirectory>(), clock)
            .RunAsync(CancellationToken.None);
    }

    private async Task DispatchAll()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>().RunOnceAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<SmsDispatcher>().RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Everything_starts_on_and_in_the_app_with_the_fields_each_message_may_use()
    {
        var teacher = await ProTeacher("Default Dinesh");
        var settings = await Settings(teacher.Client);

        Assert.Equal(9, settings.GetProperty("events").GetArrayLength());
        Assert.All(settings.GetProperty("events").EnumerateArray(), e =>
        {
            Assert.True(e.GetProperty("on").GetBoolean());
            Assert.Equal("app", e.GetProperty("way").GetString());
        });

        var overdue = Event(settings, "fee_overdue");
        Assert.Equal(["student", "class", "month", "amount", "teacher"], overdue.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["app", "whatsapp", "sms", "whatsapp_then_sms"], overdue.GetProperty("ways").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal(3, overdue.GetProperty("texts").GetArrayLength());
        Assert.All(overdue.GetProperty("texts").EnumerateArray(), t =>
        {
            Assert.Contains("{student}", t.GetProperty("default").GetString()!);
            Assert.Equal(JsonValueKind.Null, t.GetProperty("custom").ValueKind);
        });

        // Meta has approved no template for an arrival, so it can only be a text message.
        Assert.Equal(["app", "sms"], Event(settings, "arrived").GetProperty("ways").EnumerateArray().Select(w => w.GetString()));
        Assert.Equal("message_way_invalid", await Code(await Save(teacher.Client, "arrived", new { on = true, way = "whatsapp" })));

        Assert.Equal(75, Event(settings, "low_attendance").GetProperty("percent").GetInt32());
        Assert.Equal(4, settings.GetProperty("lowAttendanceWeeks").GetInt32());
    }

    [Fact]
    public async Task Switching_a_message_off_stops_it_everywhere()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Quiet Qadir");
        var classId = await DailyClass(teacher.Client, "Quiet class");
        var child = await Enrol(teacher.Client, classId, "Sanjana", guardian);
        var parent = await ParentOf(teacher.Client, child, guardian);

        Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, "payment_received", new { on = false, way = "app" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await teacher.Client.PostAsJsonAsync(
            "/api/fees/payments", new { enrolmentId = child.EnrolmentId, amount = 2000m, method = "Cash" })).StatusCode);

        Assert.Empty(await Of(parent, "child_payment"));
        Assert.False(Event(await Settings(teacher.Client), "payment_received").GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task A_fee_reminder_goes_out_on_whatsapp_with_the_teachers_own_closing_line()
    {
        var teacher = await ProTeacher("Wira");
        var classId = await DailyClass(teacher.Client, "WhatsApp fees");
        await Enrol(teacher.Client, classId, "Ishara", NewPhone());

        Assert.Equal(HttpStatusCode.OK, (await Save(
            teacher.Client, "fee_due_today", new { on = true, way = "whatsapp_then_sms", note = "Bring the receipt book." })).StatusCode);
        Assert.Equal("Bring the receipt book.", Event(await Settings(teacher.Client), "fee_due_today").GetProperty("note").GetString());

        await RunFeeJob();
        await DispatchAll();

        var sent = Assert.Single(factory.WhatsApp.Sent);
        Assert.Equal("gurubook_fee_reminder", sent.Template);
        Assert.Equal("Ishara", sent.Params[0]);
        Assert.Equal("WhatsApp fees", sent.Params[1]);
        Assert.Equal("Wira Teacher", sent.Params[4]);
        Assert.Equal("Bring the receipt book.", sent.Params[5]);
        Assert.Empty(factory.Sms.Sent);

        // The same step is never sent twice, however often the job runs.
        await RunFeeJob();
        await DispatchAll();
        Assert.Single(factory.WhatsApp.Sent);
    }

    [Fact]
    public async Task A_note_longer_than_a_line_is_refused()
    {
        var teacher = await ProTeacher("Nimal Note");
        Assert.Equal(
            "message_note_too_long",
            await Code(await Save(teacher.Client, "absent", new { on = true, way = "app", note = new string('x', 81) })));
    }

    [Fact]
    public async Task A_teacher_writes_their_own_text_message_and_can_put_ours_back()
    {
        var teacher = await ProTeacher("Tharushi");
        var classId = await DailyClass(teacher.Client, "Own words");
        await Enrol(teacher.Client, classId, "Hasini", NewPhone());

        Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, "fee_due_today", new { on = true, way = "sms" })).StatusCode);
        var mine = "Please settle {student}'s {class} fee of {amount} for {month}. - {teacher}";
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/messaging/fee_due_today/texts/en", new { text = mine })).StatusCode);

        var saved = Event(await Settings(teacher.Client), "fee_due_today").GetProperty("texts").EnumerateArray()
            .Single(t => t.GetProperty("language").GetString() == "en");
        Assert.Equal(mine, saved.GetProperty("custom").GetString());

        await RunFeeJob();
        await DispatchAll();
        var text = Assert.Single(factory.Sms.Sent).Text;
        Assert.StartsWith("Please settle Hasini's Own words fee of LKR 2,000 for ", text);
        Assert.Contains("- Tharushi Teacher", text);
        Assert.DoesNotContain("{", text);

        // A field the message does not have, and nothing at all, are both refused; an empty text puts GuruBook's words back.
        Assert.Equal(
            "message_field_unknown",
            await Code(await teacher.Client.PutAsJsonAsync("/api/messaging/fee_due_today/texts/en", new { text = "Pay for {pet}" })));
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/messaging/fee_due_today/texts/en", new { text = "  " })).StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            Event(await Settings(teacher.Client), "fee_due_today").GetProperty("texts").EnumerateArray()
                .Single(t => t.GetProperty("language").GetString() == "en").GetProperty("custom").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.Client.PutAsJsonAsync("/api/messaging/fee_due_today/texts/de", new { text = "Hallo {student}" })).StatusCode);
    }

    [Fact]
    public async Task Parents_hear_when_a_child_has_missed_too_many_classes_and_then_not_again_for_a_fortnight()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Lalith");
        var classId = await DailyClass(teacher.Client, "Low class");
        var child = await Enrol(teacher.Client, classId, "Ruwan", guardian);
        var parent = await ParentOf(teacher.Client, child, guardian);

        // Four classes held, all missed: nothing is sent before there are enough classes to judge.
        for (var back = 3; back >= 0; back--)
        {
            var date = Today.AddDays(-back);
            var sessions = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions?date={date:yyyy-MM-dd}"));
            var sessionId = sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, sessionId, new { enrolmentId = child.EnrolmentId, status = "Absent" })).StatusCode);
        }

        var told = Assert.Single(await Of(parent, "child_low_attendance"));
        Assert.Equal(child.FullName, told.GetProperty("params").GetProperty("student").GetString());
        Assert.Equal(0, told.GetProperty("params").GetProperty("percent").GetInt32());
        Assert.Equal(4, told.GetProperty("params").GetProperty("weeks").GetInt32());

        // Saving the same session again inside the fortnight says nothing more.
        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(
            teacher.Client, await TodaySession(teacher.Client, classId), new { enrolmentId = child.EnrolmentId, status = "Absent" })).StatusCode);
        Assert.Single(await Of(parent, "child_low_attendance"));
    }

    [Fact]
    public async Task A_teacher_sets_the_level_and_can_switch_the_attendance_warning_off()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Mala");
        var classId = await DailyClass(teacher.Client, "Level class");
        var child = await Enrol(teacher.Client, classId, "Dilan", guardian);
        var parent = await ParentOf(teacher.Client, child, guardian);

        Assert.Equal("message_percent_invalid", await Code(await Save(teacher.Client, "low_attendance", new { on = true, way = "app", percent = 99 })));
        Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, "low_attendance", new { on = false, way = "app", percent = 90 })).StatusCode);
        Assert.Equal(90, Event(await Settings(teacher.Client), "low_attendance").GetProperty("percent").GetInt32());

        for (var back = 3; back >= 0; back--)
        {
            var date = Today.AddDays(-back);
            var sessions = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions?date={date:yyyy-MM-dd}"));
            var sessionId = sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, sessionId, new { enrolmentId = child.EnrolmentId, status = "Absent" })).StatusCode);
        }

        Assert.Empty(await Of(parent, "child_low_attendance"));
    }

    [Fact]
    public async Task An_arrival_can_be_paid_for_as_a_text_and_says_when_the_child_came()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Arri");
        var classId = await DailyClass(teacher.Client, "Arrivals");
        var child = await Enrol(teacher.Client, classId, "Nethmi", guardian);
        await ParentOf(teacher.Client, child, guardian);

        Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, "arrived", new { on = true, way = "sms" })).StatusCode);
        var scannedAt = new DateTimeOffset(Today.Year, Today.Month, Today.Day, 16, 5, 0, TimeSpan.FromMinutes(330));
        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(
            teacher.Client, await TodaySession(teacher.Client, classId),
            new { enrolmentId = child.EnrolmentId, status = "Present", scannedAt })).StatusCode);

        await DispatchAll();
        var text = Assert.Single(factory.Sms.Sent).Text;
        Assert.Contains("Nethmi arrived at the Arrivals class at 16:05", text);
        Assert.Empty(factory.WhatsApp.Sent);
    }

    [Fact]
    public async Task A_plan_without_message_settings_sends_nothing_a_teacher_chose_to_pay_for()
    {
        var teacher = await ProTeacher("Pavi Plan");
        var classId = await DailyClass(teacher.Client, "Plan fees");
        await Enrol(teacher.Client, classId, "Kavi", NewPhone());
        Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, "fee_due_today", new { on = true, way = "sms" })).StatusCode);

        // Back to the free plan: the choice is kept, but nothing paid for goes out and the app's own reminder still does.
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var free = plans.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Free").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/subscription", new { planId = free, note = "lapsed" })).StatusCode);

        await RunFeeJob();
        await DispatchAll();
        Assert.Empty(factory.Sms.Sent);
        Assert.Empty(factory.WhatsApp.Sent);
        Assert.Equal("plan_feature_locked", await Code(await teacher.Client.GetAsync("/api/messaging")));
    }
}
