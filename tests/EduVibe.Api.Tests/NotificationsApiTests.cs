using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduVibe.Modules.Fees;
using EduVibe.Modules.Fees.Services;
using EduVibe.Modules.Notifications;
using EduVibe.Modules.Notifications.Services;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Api.Tests;

/// <summary>What teachers and students are told: who gets which notification, that repeats stay one line, and that nobody reads anybody else's.</summary>
public class NotificationsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 6000;

    private static string NewPhone() => $"077{Interlocked.Increment(ref _phoneCounter):D7}";

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> NewClass(HttpClient teacher, string title = "2027 A/L Maths")
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home class", district = "Kandy" });
        var venueId = (await Json(venue)).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2500m, slots = new[] { new { day = 1, start = "16:00", end = "18:00" } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Enrolment, string Phone, string Otp)> NewStudent(HttpClient teacher, Guid classId, string first)
    {
        var phone = NewPhone();
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Silva", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        return (body.GetProperty("row").GetProperty("enrolmentId").GetGuid(), phone, body.GetProperty("oneTimePassword").GetString()!);
    }

    private async Task<HttpClient> SignInStudent(string phone, string otp)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = otp, newPassword = "Student-pass-1", username = "s" + Guid.NewGuid().ToString("N")[..10] });
        var student = factory.CreateClient();
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);
        return student;
    }

    private static async Task<JsonElement> Notifications(HttpClient client) => await Json(await client.GetAsync("/api/notifications"));

    private static JsonElement[] Of(JsonElement list, string kind) =>
        [.. list.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("kind").GetString() == kind)];

    private async Task<(Guid Id, Guid[] Parts)> PublishedExam(HttpClient teacher, Guid classId, string name)
    {
        var response = await teacher.PostAsJsonAsync("/api/exams", new
        {
            classId, name, heldOn = new DateOnly(2026, 10, 1), parts = new[] { new { name = "Paper", maxMark = 100m } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var exam = await Json(response);
        return (exam.GetProperty("id").GetGuid(), [.. exam.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())]);
    }

    [Fact]
    public async Task Students_hear_about_published_results_once_and_only_their_own_class_does()
    {
        var teacher = await factory.NewTeacherAsync();
        var maths = await NewClass(teacher.Client, "Maths");
        var science = await NewClass(teacher.Client, "Science");
        var amara = await NewStudent(teacher.Client, maths, "Amara");
        var bimal = await NewStudent(teacher.Client, science, "Bimal");
        var amaraClient = await SignInStudent(amara.Phone, amara.Otp);
        var bimalClient = await SignInStudent(bimal.Phone, bimal.Otp);
        var exam = await PublishedExam(teacher.Client, maths, "Term 1");

        Assert.Equal(0, (await Notifications(amaraClient)).GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync($"/api/exams/{exam.Id}/publish", null)).StatusCode);

        var mine = await Notifications(amaraClient);
        Assert.Equal(1, mine.GetProperty("unread").GetInt32());
        var item = Of(mine, "results_published").Single();
        Assert.Equal("Term 1", item.GetProperty("params").GetProperty("exam").GetString());
        Assert.Equal("Maths", item.GetProperty("params").GetProperty("classTitle").GetString());
        Assert.Equal("/results", item.GetProperty("link").GetString());
        Assert.False(item.GetProperty("isRead").GetBoolean());
        Assert.Equal(0, (await Notifications(bimalClient)).GetProperty("unread").GetInt32());
        Assert.Empty(Of(await Notifications(teacher.Client), "results_published"));

        // Taking the results back and putting them out again does not tell anybody a second time; editing marks never does.
        await teacher.Client.PostAsync($"/api/exams/{exam.Id}/unpublish", null);
        await teacher.Client.PostAsync($"/api/exams/{exam.Id}/publish", null);
        Assert.Single(Of(await Notifications(amaraClient), "results_published"));
    }

    [Fact]
    public async Task Reading_a_notification_lowers_the_count_and_nobody_can_read_somebody_elses()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await NewStudent(teacher.Client, classId, "Amara");
        var bimal = await NewStudent(teacher.Client, classId, "Bimal");
        var amaraClient = await SignInStudent(amara.Phone, amara.Otp);
        var bimalClient = await SignInStudent(bimal.Phone, bimal.Otp);
        var first = await PublishedExam(teacher.Client, classId, "Term 1");
        var second = await PublishedExam(teacher.Client, classId, "Term 2");
        await teacher.Client.PostAsync($"/api/exams/{first.Id}/publish", null);
        await teacher.Client.PostAsync($"/api/exams/{second.Id}/publish", null);

        var list = await Notifications(amaraClient);
        Assert.Equal(2, list.GetProperty("unread").GetInt32());
        var id = Of(list, "results_published")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await bimalClient.PostAsync($"/api/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(2, (await Notifications(amaraClient)).GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.Client.PostAsync($"/api/notifications/{id}/read", null)).StatusCode);

        var read = await amaraClient.PostAsync($"/api/notifications/{id}/read", null);
        Assert.Equal(1, (await Json(read)).GetProperty("unread").GetInt32());
        Assert.Equal(1, (await Json(await amaraClient.GetAsync("/api/notifications/unread"))).GetProperty("unread").GetInt32());

        Assert.Equal(0, (await Json(await amaraClient.PostAsync("/api/notifications/read-all", null))).GetProperty("unread").GetInt32());
        Assert.Equal(2, (await Notifications(bimalClient)).GetProperty("unread").GetInt32());
        Assert.All((await Notifications(amaraClient)).GetProperty("items").EnumerateArray(), n => Assert.True(n.GetProperty("isRead").GetBoolean()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/notifications")).StatusCode);
    }

    [Fact]
    public async Task Students_who_join_through_a_link_are_one_line_for_the_teacher_until_they_look()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, "Physics");
        var invite = await Json(await teacher.Client.PostAsJsonAsync("/api/students/invites", new { classId }));
        var token = invite.GetProperty("token").GetString()!;
        object Registration(string name) => new { phone = NewPhone(), firstName = name, lastName = "Silva", school = "Girls High", grade = "Grade 9", password = "Student-pass-1" };

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration("Dilani"))).StatusCode);
        var one = Of(await Notifications(teacher.Client), "join_request").Single();
        Assert.Equal(1, one.GetProperty("count").GetInt32());
        Assert.Equal("Dilani Silva", one.GetProperty("params").GetProperty("student").GetString());
        Assert.Equal("Physics", one.GetProperty("params").GetProperty("classTitle").GetString());
        Assert.Equal("/students", one.GetProperty("link").GetString());

        await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration("Eranga"));
        var two = Of(await Notifications(teacher.Client), "join_request").Single();
        Assert.Equal(2, two.GetProperty("count").GetInt32());
        Assert.Equal("Eranga Silva", two.GetProperty("params").GetProperty("student").GetString());
        Assert.Equal(1, (await Notifications(teacher.Client)).GetProperty("unread").GetInt32());

        // Once the teacher has looked, the next student is a new line, not a change to one already read.
        await teacher.Client.PostAsync($"/api/notifications/{two.GetProperty("id").GetGuid()}/read", null);
        await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration("Farah"));
        var all = Of(await Notifications(teacher.Client), "join_request");
        Assert.Equal(2, all.Length);
        Assert.Equal([1, 2], all.Select(n => n.GetProperty("count").GetInt32()).Order());
        Assert.Equal(1, (await Notifications(teacher.Client)).GetProperty("unread").GetInt32());

        // A link the teacher cancelled tells nobody anything.
        await teacher.Client.PostAsync($"/api/students/invites/{invite.GetProperty("id").GetGuid()}/revoke", null);
        Assert.Equal(HttpStatusCode.Gone, (await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration("Gihan"))).StatusCode);
        Assert.Equal(2, Of(await Notifications(teacher.Client), "join_request").Length);
    }

    [Fact]
    public async Task A_student_is_told_about_a_payment_with_its_receipt()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, "Chemistry");
        var amara = await NewStudent(teacher.Client, classId, "Amara");
        var client = await SignInStudent(amara.Phone, amara.Otp);

        var paid = await teacher.Client.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = amara.Enrolment, amount = 1000m, method = "Cash" });
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);

        var item = Of(await Notifications(client), "payment_received").Single();
        Assert.Equal(1000m, item.GetProperty("params").GetProperty("amount").GetDecimal());
        Assert.Equal((await Json(paid)).GetProperty("receiptNo").GetString(), item.GetProperty("params").GetProperty("receiptNo").GetString());
        Assert.Equal("Chemistry", item.GetProperty("params").GetProperty("classTitle").GetString());
        Assert.Equal("/welcome", item.GetProperty("link").GetString());
        Assert.Empty(Of(await Notifications(teacher.Client), "payment_received"));
    }

    [Fact]
    public async Task An_extra_class_later_today_is_announced_to_the_class_and_one_already_over_is_not()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, "Biology");
        var other = await NewClass(teacher.Client, "Art");
        var amara = await NewStudent(teacher.Client, classId, "Amara");
        var bimal = await NewStudent(teacher.Client, other, "Bimal");
        var amaraClient = await SignInStudent(amara.Phone, amara.Otp);
        var bimalClient = await SignInStudent(bimal.Phone, bimal.Otp);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));

        var past = await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", new { classId, date = today.AddDays(-1), start = "09:00", end = "10:00" });
        Assert.Equal(HttpStatusCode.Created, past.StatusCode);
        Assert.Empty(Of(await Notifications(amaraClient), "extra_class"));

        var later = await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", new { classId, date = today, start = "00:01", end = "23:59", note = "Revision" });
        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
        var item = Of(await Notifications(amaraClient), "extra_class").Single();
        Assert.Equal("Biology", item.GetProperty("params").GetProperty("classTitle").GetString());
        Assert.Equal("00:01", item.GetProperty("params").GetProperty("start").GetString());
        Assert.Equal("Revision", item.GetProperty("params").GetProperty("note").GetString());
        Assert.Empty(Of(await Notifications(bimalClient), "extra_class"));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Noon in Sri Lanka on a day of this month, so the dues made for "this month" belong to the month the clock shows.
    private static FixedClock ColomboDay(int day)
    {
        var colombo = DateTime.UtcNow.AddMinutes(330);
        return new FixedClock(new DateTimeOffset(colombo.Year, colombo.Month, day, 12, 0, 0, TimeSpan.FromMinutes(330)));
    }

    private async Task RunFeeJob(TimeProvider clock)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var dues = new FeeDueService(sp.GetRequiredService<FeesDbContext>(), sp.GetRequiredService<IClassRoster>(), sp.GetRequiredService<IClassDirectory>(), clock);
        await dues.EnsureForAllTeachersAsync([FeeMonths(clock)], CancellationToken.None);
        var reminders = new FeeReminderService(
            sp.GetRequiredService<FeesDbContext>(), sp.GetRequiredService<IClassRoster>(), sp.GetRequiredService<IClassDirectory>(), sp.GetRequiredService<INotifier>(), clock);
        await reminders.RunAsync(CancellationToken.None);
    }

    private static string FeeMonths(TimeProvider clock) => clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM");

    [Fact]
    public async Task A_student_hears_when_a_fee_they_owe_is_made_and_a_teacher_gets_one_monthly_reminder_from_the_tenth()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, "Economics");
        var amara = await NewStudent(teacher.Client, classId, "Amara");
        var bimal = await NewStudent(teacher.Client, classId, "Bimal");
        var amaraClient = await SignInStudent(amara.Phone, amara.Otp);
        await teacher.Client.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = bimal.Enrolment, amount = 2500m, method = "Cash" });

        // Before the 10th the student hears, the teacher does not.
        await RunFeeJob(ColomboDay(5));
        var due = Of(await Notifications(amaraClient), "fee_due").Single();
        Assert.Equal(2500m, due.GetProperty("params").GetProperty("amount").GetDecimal());
        Assert.Equal("Economics", due.GetProperty("params").GetProperty("classTitle").GetString());
        Assert.Empty(Of(await Notifications(teacher.Client), "fees_unpaid"));

        // From the 10th the teacher hears once, however often the job runs, and only about students who have not paid.
        await RunFeeJob(ColomboDay(12));
        await RunFeeJob(ColomboDay(13));
        Assert.Single(Of(await Notifications(amaraClient), "fee_due"));
        var reminder = Of(await Notifications(teacher.Client), "fees_unpaid").Single();
        var classes = reminder.GetProperty("params").GetProperty("classes").EnumerateArray().ToArray();
        Assert.Equal("Economics", classes.Single().GetProperty("title").GetString());
        Assert.Equal(1, classes.Single().GetProperty("count").GetInt32());
        Assert.Equal($"/fees?classId={classId}", reminder.GetProperty("link").GetString());
    }

    [Fact]
    public async Task Old_notifications_are_cleared_read_ones_after_ninety_days_and_any_after_half_a_year()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var user = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var oldRead = Add(user, "a", readAt: now.AddDays(-95));
        var recentRead = Add(user, "b", readAt: now.AddDays(-10));
        var oldUnread = Add(user, "c");
        var ancient = Add(user, "d");
        db.Notifications.AddRange(oldRead, recentRead, oldUnread, ancient);
        await db.SaveChangesAsync();
        // The save stamps new rows with the real time; push the dates back to what the test needs.
        foreach (var (row, at) in new[] { (oldRead, -100), (recentRead, -100), (oldUnread, -100), (ancient, -200) })
        {
            await db.Notifications.Where(n => n.Id == row.Id).ExecuteUpdateAsync(set => set.SetProperty(n => n.UpdatedAt, now.AddDays(at)));
        }

        Assert.Equal(2, await NotificationCleanupJob.DeleteOldAsync(db, now, CancellationToken.None));
        var left = await db.Notifications.Where(n => n.RecipientId == user).Select(n => n.Kind).ToListAsync();
        Assert.Equal(["b", "c"], left.Order());
    }

    private static EduVibe.Modules.Notifications.Domain.Notification Add(Guid user, string kind, DateTimeOffset? readAt = null) =>
        new() { RecipientId = user, Kind = kind, ReadAt = readAt };

    [Fact]
    public async Task Erasing_a_teacher_deletes_their_notifications()
    {
        var teacher = await factory.NewTeacherAsync("Erased");
        var classId = await NewClass(teacher.Client);
        var invite = await Json(await teacher.Client.PostAsJsonAsync("/api/students/invites", new { classId }));
        await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{invite.GetProperty("token").GetString()}",
            new { phone = NewPhone(), firstName = "Dilani", lastName = "Silva", school = "Girls High", grade = "Grade 9", password = "Student-pass-1" });
        Assert.Single(Of(await Notifications(teacher.Client), "join_request"));

        var admin = await factory.AdminClientAsync();
        Assert.True((await admin.PutAsJsonAsync($"/api/identity/teachers/{teacher.Id}/active", new { isActive = false })).IsSuccessStatusCode);
        var export = await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export");
        Assert.Contains("Dilani Silva", await export.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "Erased Teacher" })).StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications.CountAsync(n => n.RecipientId == teacher.Id));
    }
}
