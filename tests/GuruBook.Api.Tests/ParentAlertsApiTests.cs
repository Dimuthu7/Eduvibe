using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GuruBook.Api.Tests;

/// <summary>
/// What parents are told once a teacher records a payment, saves attendance or publishes results, the corrections that follow a
/// change, and the switches both sides have: a parent mutes a topic for one child, a teacher says how much of attendance goes out.
/// </summary>
public class ParentAlertsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 7000;

    private static string NewPhone() => $"072{Interlocked.Increment(ref _phones):D7}";

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
        return teacher;
    }

    // A class that meets every day, so today always has a session, with a fee that falls due on the 10th.
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

    private static async Task<Child> Enrol(HttpClient teacher, Guid classId, string first, string guardianPhone)
    {
        var response = await teacher.PostAsJsonAsync("/api/students", new
        {
            phone = NewPhone(), firstName = first, lastName = "Silva", classId, feePlan = "Full", guardianPhone, guardianName = "Mrs Silva",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = (await Json(response)).GetProperty("row");
        return new Child(row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), $"{first} Silva");
    }

    /// <summary>Invites the parent of a child and signs that parent in, ready to read their notifications.</summary>
    private async Task<HttpClient> ParentOf(HttpClient teacher, Child child, string guardianPhone)
    {
        var invite = await teacher.PostAsync($"/api/students/{child.StudentId}/parent", null);
        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);
        if ((await Json(invite)).TryGetProperty("oneTimePassword", out var otp) && otp.ValueKind == JsonValueKind.String)
        {
            var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = guardianPhone, password = otp.GetString() });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var setup = Bearer((await Json(login)).GetProperty("accessToken").GetString()!);
            var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
                new { currentPassword = otp.GetString(), newPassword = "Parent-pass-1", username = "p" + Guid.NewGuid().ToString("N")[..10] });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            return Bearer((await Json(changed)).GetProperty("accessToken").GetString()!);
        }

        throw new InvalidOperationException("The invite gave no one-time password.");
    }

    private static async Task<JsonElement[]> Alerts(HttpClient person)
    {
        var list = await Json(await person.GetAsync("/api/notifications"));
        return [.. list.GetProperty("items").EnumerateArray()];
    }

    private static async Task<JsonElement[]> Of(HttpClient person, string kind) =>
        [.. (await Alerts(person)).Where(n => n.GetProperty("kind").GetString() == kind)];

    private static Task<HttpResponseMessage> Pay(HttpClient teacher, Guid enrolmentId, decimal amount) =>
        teacher.PostAsJsonAsync("/api/fees/payments", new { enrolmentId, amount, method = "Cash", paidOn = Today, months = Array.Empty<string>() });

    private static async Task<Guid> TodaySession(HttpClient teacher, Guid classId)
    {
        var sessions = await Json(await teacher.GetAsync($"/api/attendance/sessions?date={Today:yyyy-MM-dd}"));
        return sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SaveMarks(HttpClient teacher, Guid sessionId, params object[] marks) =>
        teacher.PutAsJsonAsync($"/api/attendance/sessions/{sessionId}/marks", new { marks });

    [Fact]
    public async Task A_payment_tells_the_parent_with_the_child_named_and_taking_it_back_corrects_it()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Padma");
        var classId = await DailyClass(teacher.Client, "Payments class");
        var child = await Enrol(teacher.Client, classId, "Amara", guardian);
        var parent = await ParentOf(teacher.Client, child, guardian);

        var paid = await Pay(teacher.Client, child.EnrolmentId, 2000m);
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        var receiptNo = (await Json(paid)).GetProperty("receiptNo").GetString();

        var told = Assert.Single(await Of(parent, "child_payment"));
        Assert.Equal(child.FullName, told.GetProperty("params").GetProperty("student").GetString());
        Assert.Equal(receiptNo, told.GetProperty("params").GetProperty("receiptNo").GetString());
        Assert.Equal(2000m, told.GetProperty("params").GetProperty("amount").GetDecimal());
        Assert.Equal("/family", told.GetProperty("link").GetString());

        var paymentId = (await Json(paid)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{paymentId}/void", new { reason = "Entered twice" })).StatusCode);

        var corrected = Assert.Single(await Of(parent, "child_payment_voided"));
        Assert.Equal(receiptNo, corrected.GetProperty("params").GetProperty("receiptNo").GetString());
        Assert.Single(await Of(parent, "child_payment"));
    }

    [Fact]
    public async Task A_free_teacher_tells_nobody_because_the_parent_portal_is_not_in_the_plan()
    {
        var guardian = NewPhone();
        var pro = await ProTeacher("Pro Pamela");
        var classId = await DailyClass(pro.Client, "Linked class");
        var child = await Enrol(pro.Client, classId, "Kamal", guardian);
        var parent = await ParentOf(pro.Client, child, guardian);

        // The same child, at a teacher on the free plan: that teacher's payment reaches nobody.
        var free = await factory.NewTeacherAsync("Freda");
        var freeClass = await DailyClass(free.Client, "Free class");
        var alsoThere = await Enrol(free.Client, freeClass, "Kamal", guardian);
        Assert.Equal(HttpStatusCode.Created, (await Pay(free.Client, alsoThere.EnrolmentId, 500m)).StatusCode);
        Assert.Empty(await Of(parent, "child_payment"));

        Assert.Equal(HttpStatusCode.Created, (await Pay(pro.Client, child.EnrolmentId, 500m)).StatusCode);
        Assert.Single(await Of(parent, "child_payment"));
    }

    [Fact]
    public async Task Saving_attendance_tells_the_parent_once_per_child_with_the_scan_time_and_only_a_change_goes_out_again()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Anula");
        var classId = await DailyClass(teacher.Client, "Attendance class");
        var child = await Enrol(teacher.Client, classId, "Nimali", guardian);
        var other = await Enrol(teacher.Client, classId, "Ravi", NewPhone());
        var parent = await ParentOf(teacher.Client, child, guardian);
        var sessionId = await TodaySession(teacher.Client, classId);

        var scannedAt = new DateTimeOffset(Today.Year, Today.Month, Today.Day, 10, 32, 0, TimeSpan.FromMinutes(330));
        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(
            teacher.Client, sessionId,
            new { enrolmentId = child.EnrolmentId, status = "Present", scannedAt },
            new { enrolmentId = other.EnrolmentId, status = "Absent" })).StatusCode);

        var arrived = Assert.Single(await Of(parent, "child_attended"));
        Assert.Equal(child.FullName, arrived.GetProperty("params").GetProperty("student").GetString());
        Assert.Equal("10:32", arrived.GetProperty("params").GetProperty("at").GetString());
        Assert.False(arrived.GetProperty("params").GetProperty("late").GetBoolean());

        // Saving the same marks again says nothing more; only a correction does.
        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, sessionId, new { enrolmentId = child.EnrolmentId, status = "Present" })).StatusCode);
        Assert.Single(await Of(parent, "child_attended"));

        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, sessionId, new { enrolmentId = child.EnrolmentId, status = "Absent" })).StatusCode);
        var missed = Assert.Single(await Of(parent, "child_absent"));
        Assert.Equal(Today.ToString("yyyy-MM-dd"), missed.GetProperty("params").GetProperty("date").GetString());
    }

    [Fact]
    public async Task A_late_student_counts_as_arrived_and_absences_only_leaves_out_the_ones_who_came()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Chandra");
        var classId = await DailyClass(teacher.Client, "Scope class");
        var child = await Enrol(teacher.Client, classId, "Suresh", guardian);
        var parent = await ParentOf(teacher.Client, child, guardian);
        var sessionId = await TodaySession(teacher.Client, classId);

        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, sessionId, new { enrolmentId = child.EnrolmentId, status = "Late" })).StatusCode);
        var late = Assert.Single(await Of(parent, "child_attended"));
        Assert.True(late.GetProperty("params").GetProperty("late").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/notifications/parent-alerts", new { attendanceAlerts = "absences" })).StatusCode);
        var second = await DailyClass(teacher.Client, "Scope class two");
        var alsoMine = await Enrol(teacher.Client, second, "Suresh", guardian);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync($"/api/students/{alsoMine.StudentId}/parent", null)).StatusCode);
        var secondSession = await TodaySession(teacher.Client, second);

        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, secondSession, new { enrolmentId = alsoMine.EnrolmentId, status = "Present" })).StatusCode);
        Assert.Single(await Of(parent, "child_attended"));

        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, secondSession, new { enrolmentId = alsoMine.EnrolmentId, status = "Absent" })).StatusCode);
        Assert.Single(await Of(parent, "child_absent"));

        // Off means nothing at all, not even an absence.
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync("/api/notifications/parent-alerts", new { attendanceAlerts = "off" })).StatusCode);
        var third = await DailyClass(teacher.Client, "Scope class three");
        var thirdChild = await Enrol(teacher.Client, third, "Suresh", guardian);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync($"/api/students/{thirdChild.StudentId}/parent", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, await TodaySession(teacher.Client, third), new { enrolmentId = thirdChild.EnrolmentId, status = "Absent" })).StatusCode);
        Assert.Single(await Of(parent, "child_absent"));

        Assert.Equal("attendance_alerts_invalid", await Code(await teacher.Client.PutAsJsonAsync("/api/notifications/parent-alerts", new { attendanceAlerts = "sometimes" })));
        Assert.Equal("all", (await Json(await (await ProTeacher("Default Dilani")).Client.GetAsync("/api/notifications/parent-alerts"))).GetProperty("attendanceAlerts").GetString());
    }

    [Fact]
    public async Task Published_results_reach_the_parent_and_a_changed_mark_is_corrected_once()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Ruwan");
        var classId = await DailyClass(teacher.Client, "Results class");
        var child = await Enrol(teacher.Client, classId, "Dilani", guardian);
        var parent = await ParentOf(teacher.Client, child, guardian);

        var exam = await Json(await teacher.Client.PostAsJsonAsync("/api/exams", new
        {
            classId, name = "Term 1", heldOn = Today.AddDays(-1), parts = new[] { new { name = "Paper", maxMark = 100m } },
        }));
        var examId = exam.GetProperty("id").GetGuid();
        var partId = exam.GetProperty("parts").EnumerateArray().First().GetProperty("id").GetGuid();

        async Task Mark(decimal value) => Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}/marks", new
        {
            rows = new[] { new { enrolmentId = child.EnrolmentId, absent = false, marks = new Dictionary<Guid, decimal?> { [partId] = value } } },
        })).StatusCode);

        await Mark(60m);
        Assert.Empty(await Of(parent, "child_result_changed"));

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync($"/api/exams/{examId}/publish", null)).StatusCode);
        var published = Assert.Single(await Of(parent, "child_results"));
        Assert.Equal("Term 1", published.GetProperty("params").GetProperty("exam").GetString());
        Assert.Equal(child.FullName, published.GetProperty("params").GetProperty("student").GetString());

        // Saving the same mark again changes nothing, so nobody is told; a different mark is a correction.
        await Mark(60m);
        Assert.Empty(await Of(parent, "child_result_changed"));

        await Mark(72m);
        var corrected = Assert.Single(await Of(parent, "child_result_changed"));
        Assert.Equal(1, corrected.GetProperty("count").GetInt32());

        // A second correction joins the line that is still unread instead of adding another.
        await Mark(74m);
        Assert.Equal(2, Assert.Single(await Of(parent, "child_result_changed")).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task A_parent_mutes_one_topic_for_one_child_and_still_hears_about_the_other()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Mala");
        var classId = await DailyClass(teacher.Client, "Mute class");
        var one = await Enrol(teacher.Client, classId, "Ishara", guardian);
        var two = await Enrol(teacher.Client, classId, "Kavindu", guardian);
        var parent = await ParentOf(teacher.Client, one, guardian);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync($"/api/students/{two.StudentId}/parent", null)).StatusCode);

        var muted = await parent.PutAsJsonAsync("/api/notifications/alerts", new { studentId = one.StudentId, topic = "payments", on = false });
        Assert.Equal(HttpStatusCode.OK, muted.StatusCode);
        var silences = (await Json(muted)).GetProperty("muted").EnumerateArray().ToArray();
        Assert.Equal(one.StudentId, Assert.Single(silences).GetProperty("studentId").GetGuid());

        Assert.Equal(HttpStatusCode.Created, (await Pay(teacher.Client, one.EnrolmentId, 300m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Pay(teacher.Client, two.EnrolmentId, 400m)).StatusCode);
        var heard = await Of(parent, "child_payment");
        Assert.Equal(two.FullName, Assert.Single(heard).GetProperty("params").GetProperty("student").GetString());

        // Attendance is a topic of its own, so muting payments leaves it alone.
        var sessionId = await TodaySession(teacher.Client, classId);
        Assert.Equal(HttpStatusCode.OK, (await SaveMarks(teacher.Client, sessionId, new { enrolmentId = one.EnrolmentId, status = "Absent" })).StatusCode);
        Assert.Contains(await Of(parent, "child_absent"), n => n.GetProperty("params").GetProperty("student").GetString() == one.FullName);

        // Switching it back on is the same call, and a topic nobody knows is refused.
        Assert.Empty((await Json(await parent.PutAsJsonAsync("/api/notifications/alerts", new { studentId = one.StudentId, topic = "payments", on = true })))
            .GetProperty("muted").EnumerateArray());
        Assert.Equal("alert_topic_invalid", await Code(await parent.PutAsJsonAsync("/api/notifications/alerts", new { studentId = one.StudentId, topic = "weather", on = false })));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/notifications/alerts")).StatusCode);
    }

    [Fact]
    public async Task Only_a_teacher_on_the_parent_portal_plan_can_read_or_change_what_parents_are_told()
    {
        var free = await factory.NewTeacherAsync("Frank");
        Assert.Equal("plan_feature_locked", await Code(await free.Client.GetAsync("/api/notifications/parent-alerts")));
        Assert.Equal("plan_feature_locked", await Code(await free.Client.PutAsJsonAsync("/api/notifications/parent-alerts", new { attendanceAlerts = "off" })));

        var guardian = NewPhone();
        var pro = await ProTeacher("Pavi");
        var classId = await DailyClass(pro.Client, "Plan class");
        var child = await Enrol(pro.Client, classId, "Tharindu", guardian);
        var parent = await ParentOf(pro.Client, child, guardian);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/notifications/parent-alerts")).StatusCode);
    }
}
