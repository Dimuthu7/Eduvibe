using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduVibe.Modules.Attendance;
using EduVibe.Shared.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Api.Tests;

public class AttendanceApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 5000;

    private static string NewPhone() => $"076{Interlocked.Increment(ref _phoneCounter):D7}";

    // Today in Sri Lanka, the same way the API works it out.
    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    private static int Weekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    // A class that meets every day of the week, so any date has a session.
    private async Task<Guid> DailyClass(HttpClient teacher, string title = "2027 A/L Maths")
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home class", district = "Kandy" });
        var venueId = (await Json(venue)).GetProperty("id").GetGuid();
        var slots = Enumerable.Range(1, 7).Select(day => new { day, start = "16:00", end = "18:00" }).ToArray();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2500m, slots,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private async Task<(Guid EnrolmentId, string Phone)> AddStudent(HttpClient teacher, Guid classId, string first)
    {
        var phone = NewPhone();
        var response = await teacher.PostAsJsonAsync("/api/students", new
        {
            phone, firstName = first, lastName = "Perera", classId, feePlan = "Full",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await Json(response)).GetProperty("row").GetProperty("enrolmentId").GetGuid(), phone);
    }

    private static async Task<JsonElement> Sessions(HttpClient teacher, DateOnly date) =>
        await Json(await teacher.GetAsync($"/api/attendance/sessions?date={date:yyyy-MM-dd}"));

    private static Task<HttpResponseMessage> Save(HttpClient teacher, Guid sessionId, params (Guid Enrolment, string Status)[] marks) =>
        teacher.PutAsJsonAsync($"/api/attendance/sessions/{sessionId}/marks", new
        {
            marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray(),
        });

    private static Guid SessionOf(JsonElement sessions, Guid classId) =>
        sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId && !s.GetProperty("isExtra").GetBoolean()).GetProperty("id").GetGuid();

    [Fact]
    public async Task Opening_a_day_makes_the_sessions_from_the_timetable_once()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);

        var first = await Sessions(teacher.Client, Today);
        var second = await Sessions(teacher.Client, Today);

        var session = Assert.Single(first.EnumerateArray());
        Assert.Equal(classId, session.GetProperty("classId").GetGuid());
        Assert.Equal("16:00", session.GetProperty("start").GetString());
        Assert.False(session.GetProperty("taken").GetBoolean());
        Assert.Equal(session.GetProperty("id").GetGuid(), Assert.Single(second.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_class_that_does_not_meet_that_day_has_no_session()
    {
        var teacher = await factory.NewTeacherAsync();
        var venue = await teacher.Client.PostAsJsonAsync("/api/classes/venues", new { name = "Hall", district = "Kandy" });
        var otherDay = Weekday(Today) % 7 + 1;
        await teacher.Client.PostAsJsonAsync("/api/classes/classes", new
        {
            title = "Other day", subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId = (await Json(venue)).GetProperty("id").GetGuid(), monthlyFee = 1000m,
            slots = new[] { new { day = otherDay, start = "10:00", end = "11:00" } },
        });

        Assert.Empty((await Sessions(teacher.Client, Today)).EnumerateArray());
    }

    [Fact]
    public async Task Everyone_is_present_by_default_and_saved_marks_come_back()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);
        var (amara, _) = await AddStudent(teacher.Client, classId, "Amara");
        var (bandula, _) = await AddStudent(teacher.Client, classId, "Bandula");
        var (chamari, _) = await AddStudent(teacher.Client, classId, "Chamari");
        var sessionId = SessionOf(await Sessions(teacher.Client, Today), classId);

        var before = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions/{sessionId}"));
        Assert.All(before.GetProperty("students").EnumerateArray(), s => Assert.Equal("Present", s.GetProperty("status").GetString()));

        var saved = await Save(teacher.Client, sessionId, (bandula, "Absent"), (chamari, "Late"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var summary = await Json(saved);
        Assert.True(summary.GetProperty("taken").GetBoolean());
        Assert.Equal(1, summary.GetProperty("present").GetInt32());
        Assert.Equal(1, summary.GetProperty("late").GetInt32());
        Assert.Equal(1, summary.GetProperty("absent").GetInt32());

        var after = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions/{sessionId}"));
        var statuses = after.GetProperty("students").EnumerateArray().ToDictionary(s => s.GetProperty("enrolmentId").GetGuid(), s => s.GetProperty("status").GetString());
        Assert.Equal("Present", statuses[amara]);
        Assert.Equal("Absent", statuses[bandula]);
        Assert.Equal("Late", statuses[chamari]);

        // Saving again replaces the earlier marks: nobody listed means everybody present.
        await Save(teacher.Client, sessionId);
        var again = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions/{sessionId}"));
        Assert.All(again.GetProperty("students").EnumerateArray(), s => Assert.Equal("Present", s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Marks_can_be_changed_for_a_week_but_not_after_and_never_for_the_future()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);
        await AddStudent(teacher.Client, classId, "Amara");

        var weekAgo = Today.AddDays(-7);
        var ok = SessionOf(await Sessions(teacher.Client, weekAgo), classId);
        Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, ok)).StatusCode);

        // Eight days back is outside the window: nothing is made for it, so it cannot be marked or added.
        Assert.Empty((await Sessions(teacher.Client, Today.AddDays(-8))).EnumerateArray());
        var old = await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", new { classId, date = Today.AddDays(-8), start = "09:00", end = "10:00" });
        Assert.Equal("outside_edit_window", await Code(old));

        var future = await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", new { classId, date = Today.AddDays(1), start = "09:00", end = "10:00" });
        Assert.Equal("date_in_future", await Code(future));
        Assert.Empty((await Sessions(teacher.Client, Today.AddDays(1))).EnumerateArray());
    }

    [Fact]
    public async Task An_extra_class_can_be_added_marked_and_removed_only_before_it_is_marked()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);
        var (amara, _) = await AddStudent(teacher.Client, classId, "Amara");
        var body = new { classId, date = Today, start = "08:00", end = "09:30", note = "Revision" };

        var created = await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var extra = await Json(created);
        Assert.True(extra.GetProperty("isExtra").GetBoolean());
        Assert.Equal("Revision", extra.GetProperty("note").GetString());
        Assert.Equal("session_exists", await Code(await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", body)));
        Assert.Equal("time_range_invalid", await Code(await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", new { classId, date = Today, start = "10:00", end = "09:00" })));

        var spare = await Json(await teacher.Client.PostAsJsonAsync("/api/attendance/sessions", new { classId, date = Today, start = "12:00", end = "13:00" }));
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/attendance/sessions/{spare.GetProperty("id").GetGuid()}")).StatusCode);

        var id = extra.GetProperty("id").GetGuid();
        await Save(teacher.Client, id, (amara, "Absent"));
        Assert.Equal("session_not_removable", await Code(await teacher.Client.DeleteAsync($"/api/attendance/sessions/{id}")));

        // A timetable session is never removed.
        var timetable = SessionOf(await Sessions(teacher.Client, Today), classId);
        Assert.Equal("session_not_removable", await Code(await teacher.Client.DeleteAsync($"/api/attendance/sessions/{timetable}")));
    }

    [Fact]
    public async Task A_student_who_is_not_in_the_class_is_refused()
    {
        var teacher = await factory.NewTeacherAsync();
        var maths = await DailyClass(teacher.Client);
        var science = await DailyClass(teacher.Client, "Science");
        var (outsider, _) = await AddStudent(teacher.Client, science, "Outsider");
        var sessionId = SessionOf(await Sessions(teacher.Client, Today), maths);

        Assert.Equal("student_not_in_class", await Code(await Save(teacher.Client, sessionId, (outsider, "Absent"))));
        Assert.Equal("mark_invalid", await Code(await Save(teacher.Client, sessionId, (outsider, "Sleeping"))));
    }

    [Fact]
    public async Task Summary_counts_taken_sessions_and_treats_late_as_attended()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);
        var (amara, _) = await AddStudent(teacher.Client, classId, "Amara");
        var (bandula, _) = await AddStudent(teacher.Client, classId, "Bandula");

        // Four days: Bandula is absent, late, present, and the fourth day is never marked.
        var statuses = new[] { "Absent", "Late", "Present", null };
        for (var back = 0; back < 4; back++)
        {
            var sessionId = SessionOf(await Sessions(teacher.Client, Today.AddDays(-back)), classId);
            if (statuses[back] is { } status)
            {
                Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, sessionId, (bandula, status))).StatusCode);
            }
        }

        var from = Today.AddDays(-6);
        var summary = await Json(await teacher.Client.GetAsync($"/api/attendance/summary?classId={classId}&from={from:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"));
        Assert.Equal(3, summary.GetProperty("sessions").GetInt32());
        var rows = summary.GetProperty("students").EnumerateArray().ToDictionary(s => s.GetProperty("enrolmentId").GetGuid());
        Assert.Equal(100, rows[amara].GetProperty("percent").GetInt32());
        var bandulaRow = rows[bandula];
        Assert.Equal(1, bandulaRow.GetProperty("present").GetInt32());
        Assert.Equal(1, bandulaRow.GetProperty("late").GetInt32());
        Assert.Equal(1, bandulaRow.GetProperty("absent").GetInt32());
        Assert.Equal(67, bandulaRow.GetProperty("percent").GetInt32());
        Assert.Equal(83, summary.GetProperty("percent").GetInt32());

        var narrow = await Json(await teacher.Client.GetAsync($"/api/attendance/summary?classId={classId}&from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"));
        Assert.Equal(1, narrow.GetProperty("sessions").GetInt32());
        Assert.Equal("range_invalid", await Code(await teacher.Client.GetAsync($"/api/attendance/summary?classId={classId}&from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}")));
    }

    [Fact]
    public async Task Student_report_lists_each_marked_class_in_date_order_and_defaults_to_this_month()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);
        var (bandula, phone) = await AddStudent(teacher.Client, classId, "Bandula");

        // Three days back, two back and today: absent, late, present. Yesterday is never marked.
        var statuses = new[] { "Present", null, "Late", "Absent" };
        for (var back = 0; back < 4; back++)
        {
            var sessionId = SessionOf(await Sessions(teacher.Client, Today.AddDays(-back)), classId);
            if (statuses[back] is { } status)
            {
                Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, sessionId, (bandula, status))).StatusCode);
            }
        }

        var report = await Json(await teacher.Client.GetAsync(
            $"/api/attendance/enrolments/{bandula}/report?from={Today.AddDays(-6):yyyy-MM-dd}&to={Today:yyyy-MM-dd}"));
        Assert.Equal("Bandula Perera", report.GetProperty("fullName").GetString());
        Assert.Equal("2027 A/L Maths", report.GetProperty("classTitle").GetString());
        Assert.Equal(3, report.GetProperty("sessions").GetInt32());
        Assert.Equal(1, report.GetProperty("present").GetInt32());
        Assert.Equal(1, report.GetProperty("late").GetInt32());
        Assert.Equal(1, report.GetProperty("absent").GetInt32());
        Assert.Equal(67, report.GetProperty("percent").GetInt32());
        var entries = report.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["Absent", "Late", "Present"], entries.Select(e => e.GetProperty("status").GetString()));
        Assert.Equal(Today.AddDays(-3).ToString("yyyy-MM-dd"), entries[0].GetProperty("date").GetString());
        Assert.Equal("16:00:00", entries[0].GetProperty("start").GetString());

        // Without dates it is the first of this month to today, which holds today's session at least.
        var thisMonth = await Json(await teacher.Client.GetAsync($"/api/attendance/enrolments/{bandula}/report"));
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1).ToString("yyyy-MM-dd"), thisMonth.GetProperty("from").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), thisMonth.GetProperty("to").GetString());
        Assert.Contains(thisMonth.GetProperty("entries").EnumerateArray(), e => e.GetProperty("date").GetString() == Today.ToString("yyyy-MM-dd"));

        Assert.Equal("range_invalid", await Code(await teacher.Client.GetAsync(
            $"/api/attendance/enrolments/{bandula}/report?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}")));
        Assert.Equal("enrolment_not_found", await Code(await teacher.Client.GetAsync($"/api/attendance/enrolments/{Guid.NewGuid()}/report")));
        Assert.NotEqual(HttpStatusCode.OK, (await factory.CreateClient().GetAsync($"/api/attendance/enrolments/{bandula}/report")).StatusCode);

        // Enrolled in a second class too: both are offered so the screen can switch, and each has its own marks.
        var second = await DailyClass(teacher.Client, "2027 A/L Physics");
        var enrol = await teacher.Client.PostAsJsonAsync("/api/students", new
        {
            phone, firstName = "x", lastName = "y", classId = second, feePlan = "Full", enrolExisting = true,
        });
        Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        var secondEnrolment = (await Json(enrol)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
        var both = await Json(await teacher.Client.GetAsync($"/api/attendance/enrolments/{bandula}/report"));
        Assert.Equal(
            new[] { "2027 A/L Maths", "2027 A/L Physics" },
            both.GetProperty("classes").EnumerateArray().Select(c => c.GetProperty("title").GetString()!).Order().ToArray());
        var physics = await Json(await teacher.Client.GetAsync($"/api/attendance/enrolments/{secondEnrolment}/report"));
        Assert.Equal("2027 A/L Physics", physics.GetProperty("classTitle").GetString());
        Assert.Equal(0, physics.GetProperty("sessions").GetInt32());
        Assert.Equal(0, physics.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task A_teacher_cannot_read_another_teachers_student_report()
    {
        var owner = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Sunil");
        var classId = await DailyClass(owner.Client);
        var (enrolment, _) = await AddStudent(owner.Client, classId, "Amara");

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/attendance/enrolments/{enrolment}/report")).StatusCode);
    }

    [Fact]
    public async Task A_teacher_cannot_see_or_change_another_teachers_sessions()
    {
        var owner = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Sunil");
        var classId = await DailyClass(owner.Client);
        var sessionId = SessionOf(await Sessions(owner.Client, Today), classId);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/attendance/sessions/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Save(other.Client, sessionId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync($"/api/attendance/sessions/{sessionId}")).StatusCode);
        Assert.Empty((await Sessions(other.Client, Today)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/attendance/summary?classId={classId}")).StatusCode);
        var extra = await other.Client.PostAsJsonAsync("/api/attendance/sessions", new { classId, date = Today, start = "08:00", end = "09:00" });
        Assert.Equal(HttpStatusCode.NotFound, extra.StatusCode);
    }

    [Fact]
    public async Task A_student_sees_their_own_classes_times_and_attendance_only()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);
        var phone = NewPhone();
        var created = await teacher.Client.PostAsJsonAsync("/api/students", new { phone, firstName = "Dilani", lastName = "Silva", classId, feePlan = "Full" });
        var enrolment = (await Json(created)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
        var otp = (await Json(created)).GetProperty("oneTimePassword").GetString()!;
        var (other, _) = await AddStudent(teacher.Client, classId, "Other");

        foreach (var (back, status) in new[] { (0, "Absent"), (1, "Present"), (2, "Late"), (3, "Present") })
        {
            var sessionId = SessionOf(await Sessions(teacher.Client, Today.AddDays(-back)), classId);
            await Save(teacher.Client, sessionId, (enrolment, status), (other, "Absent"));
        }

        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        var first = (await Json(login)).GetProperty("accessToken").GetString()!;
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = otp, newPassword = "Student-pass-1", username = "s" + Guid.NewGuid().ToString("N")[..10] });
        var student = factory.CreateClient();
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);

        var classes = await Json(await student.GetAsync("/api/students/me/classes"));
        var mine = Assert.Single(classes.EnumerateArray());
        Assert.Equal("2027 A/L Maths", mine.GetProperty("classTitle").GetString());
        Assert.Equal(7, mine.GetProperty("slots").GetArrayLength());

        var attendance = Assert.Single((await Json(await student.GetAsync("/api/attendance/mine"))).EnumerateArray());
        Assert.Equal(classId, attendance.GetProperty("classId").GetGuid());
        Assert.Equal(4, attendance.GetProperty("sessions").GetInt32());
        Assert.Equal(1, attendance.GetProperty("absent").GetInt32());
        Assert.Equal(75, attendance.GetProperty("percent").GetInt32());
        Assert.Equal("Absent", attendance.GetProperty("recent")[0].GetProperty("status").GetString());

        // The teacher screens are closed to a student, and the student screens to a teacher.
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/attendance/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/attendance/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/students/me/classes")).StatusCode);
    }

    [Fact]
    public async Task Every_mark_and_every_change_to_it_is_logged_with_the_teacher_who_made_it()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client);
        var (amara, _) = await AddStudent(teacher.Client, classId, "Amara");
        var sessionId = SessionOf(await Sessions(teacher.Client, Today), classId);

        await Save(teacher.Client, sessionId, (amara, "Absent"));
        await Save(teacher.Client, sessionId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        var entries = await db.Set<AuditLogEntry>().Where(e => e.Entity.Contains("AttendanceMark")).ToListAsync();
        var actions = entries.OrderBy(e => e.Action).Select(e => e.Action).ToList();
        Assert.Contains(AuditAction.Created, actions);
        var update = Assert.Single(entries, e => e.Action == AuditAction.Updated);
        // The log keeps the enum's number: Present is 0 and Absent is 1.
        Assert.Contains("\"Status\":1", update.Before);
        Assert.Contains("\"Status\":0", update.After);
        Assert.NotNull(update.UserId);
    }
}
