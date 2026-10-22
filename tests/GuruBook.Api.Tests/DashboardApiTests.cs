using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GuruBook.Api.Tests;

/// <summary>The teacher dashboard: the saved layout, the attendance watch and the exam progress.</summary>
public class DashboardApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 9000;

    private static string NewPhone() => $"071{Interlocked.Increment(ref _phoneCounter):D7}";

    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private static object Widget(string key, bool enabled) => new { key, enabled };

    [Fact]
    public async Task A_new_teacher_has_no_saved_layout_and_a_saved_one_comes_back_in_order()
    {
        var teacher = await factory.NewTeacherAsync();
        var none = await Json(await teacher.Client.GetAsync("/api/dashboard/layout"));
        Assert.False(none.GetProperty("customised").GetBoolean());
        Assert.Equal(0, none.GetProperty("widgets").GetArrayLength());

        var saved = await teacher.Client.PutAsJsonAsync("/api/dashboard/layout", new { widgets = new[] { Widget("fees", true), Widget("today", true), Widget("exams", false) } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var back = await Json(await teacher.Client.GetAsync("/api/dashboard/layout"));
        Assert.True(back.GetProperty("customised").GetBoolean());
        Assert.Equal(
            ["fees:True", "today:True", "exams:False"],
            back.GetProperty("widgets").EnumerateArray().Select(w => $"{w.GetProperty("key").GetString()}:{w.GetProperty("enabled").GetBoolean()}"));

        // Saving again replaces the list rather than adding to it.
        await teacher.Client.PutAsJsonAsync("/api/dashboard/layout", new { widgets = new[] { Widget("today", false) } });
        var replaced = await Json(await teacher.Client.GetAsync("/api/dashboard/layout"));
        Assert.Equal(1, replaced.GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task Each_teacher_has_their_own_layout_and_can_go_back_to_the_default()
    {
        var one = await factory.NewTeacherAsync();
        var two = await factory.NewTeacherAsync("Sunil");
        await one.Client.PutAsJsonAsync("/api/dashboard/layout", new { widgets = new[] { Widget("fees", true) } });

        var other = await Json(await two.Client.GetAsync("/api/dashboard/layout"));
        Assert.False(other.GetProperty("customised").GetBoolean());

        var reset = await one.Client.DeleteAsync("/api/dashboard/layout");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.False((await Json(await one.Client.GetAsync("/api/dashboard/layout"))).GetProperty("customised").GetBoolean());
    }

    [Fact]
    public async Task A_layout_with_a_bad_key_a_repeat_or_too_many_widgets_is_refused()
    {
        var teacher = await factory.NewTeacherAsync();
        async Task<string?> Try(params object[] widgets) => await Code(await teacher.Client.PutAsJsonAsync("/api/dashboard/layout", new { widgets }));

        Assert.Equal("layout_invalid", await Try(Widget("Bad Key", true)));
        Assert.Equal("layout_invalid", await Try(Widget("", true)));
        Assert.Equal("layout_invalid", await Try(Widget("today", true), Widget("today", false)));
        Assert.Equal("layout_invalid", await Try(new { key = "today" }));
        Assert.Equal("layout_invalid", await Try([.. Enumerable.Range(0, 31).Select(i => Widget($"w{i}", true))]));
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.Client.PutAsJsonAsync("/api/dashboard/layout", new { })).StatusCode);
        Assert.Null(await Try());
    }

    [Fact]
    public async Task The_layout_needs_a_teacher_sign_in()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/dashboard/layout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/attendance/watch")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/exams/progress")).StatusCode);
    }

    private async Task<Guid> DailyClass(HttpClient teacher, string title)
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

    private static async Task<Guid> AddStudent(HttpClient teacher, Guid classId, string first)
    {
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone = NewPhone(), firstName = first, lastName = "Perera", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
    }

    // Marks a day's class: everyone present except the students named.
    private static async Task Mark(HttpClient teacher, Guid classId, DateOnly day, params (Guid Enrolment, string Status)[] marks)
    {
        var sessions = await Json(await teacher.GetAsync($"/api/attendance/sessions?date={day:yyyy-MM-dd}"));
        var id = sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
        var saved = await teacher.PutAsJsonAsync($"/api/attendance/sessions/{id}/marks", new
        {
            marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [Fact]
    public async Task The_watch_picks_students_who_missed_the_latest_classes_or_came_rarely_and_leaves_the_rest()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client, "2027 A/L Maths");
        var amara = await AddStudent(teacher.Client, classId, "Amara");     // missed the last two
        var bandula = await AddStudent(teacher.Client, classId, "Bandula"); // missed only the first
        var chamari = await AddStudent(teacher.Client, classId, "Chamari"); // always there
        var dilani = await AddStudent(teacher.Client, classId, "Dilani");   // came to 2 of 4, but is back
        var eranga = await AddStudent(teacher.Client, classId, "Eranga");   // late counts as attended

        await Mark(teacher.Client, classId, Today.AddDays(-4), (dilani, "Absent"), (amara, "Present"), (eranga, "Late"));
        await Mark(teacher.Client, classId, Today.AddDays(-3), (dilani, "Absent"), (bandula, "Absent"), (eranga, "Late"));
        await Mark(teacher.Client, classId, Today.AddDays(-2), (amara, "Absent"), (eranga, "Late"));
        await Mark(teacher.Client, classId, Today.AddDays(-1), (amara, "Absent"), (eranga, "Late"));

        var watch = await Json(await teacher.Client.GetAsync("/api/attendance/watch"));
        Assert.Equal(30, watch.GetProperty("windowDays").GetInt32());
        Assert.Equal(2, watch.GetProperty("total").GetInt32());

        var rows = watch.GetProperty("rows").EnumerateArray().ToList();
        // The one who missed the most in a row comes first.
        Assert.Equal(["Amara Perera", "Dilani Perera"], rows.Select(r => r.GetProperty("fullName").GetString()));
        Assert.DoesNotContain(rows, r => r.GetProperty("enrolmentId").GetGuid() == chamari || r.GetProperty("enrolmentId").GetGuid() == eranga);

        var a = rows.Single(r => r.GetProperty("enrolmentId").GetGuid() == amara);
        Assert.Equal(2, a.GetProperty("missedInARow").GetInt32());
        Assert.Equal(4, a.GetProperty("sessions").GetInt32());
        Assert.Equal(50, a.GetProperty("percent").GetInt32());
        Assert.Equal(Today.AddDays(-3).ToString("yyyy-MM-dd"), a.GetProperty("lastAttended").GetString());
        Assert.Equal("2027 A/L Maths", a.GetProperty("classTitle").GetString());

        // Bandula missed one class in a row and has had 3 classes: neither rule fires yet.
        Assert.DoesNotContain(rows, r => r.GetProperty("enrolmentId").GetGuid() == bandula);

        var d = rows.Single(r => r.GetProperty("enrolmentId").GetGuid() == dilani);
        Assert.Equal(0, d.GetProperty("missedInARow").GetInt32());
        Assert.Equal(50, d.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task The_watch_is_empty_without_saved_attendance_and_only_shows_the_teachers_own_students()
    {
        var teacher = await factory.NewTeacherAsync();
        var empty = await Json(await teacher.Client.GetAsync("/api/attendance/watch"));
        Assert.Equal(0, empty.GetProperty("total").GetInt32());

        var other = await factory.NewTeacherAsync("Sunil");
        var classId = await DailyClass(other.Client, "Sunil's class");
        var student = await AddStudent(other.Client, classId, "Kasun");
        await Mark(other.Client, classId, Today.AddDays(-2), (student, "Absent"));
        await Mark(other.Client, classId, Today.AddDays(-1), (student, "Absent"));

        Assert.Equal(1, (await Json(await other.Client.GetAsync("/api/attendance/watch"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Json(await teacher.Client.GetAsync("/api/attendance/watch"))).GetProperty("total").GetInt32());
    }

    private static async Task<(Guid Id, Guid[] Parts)> NewExam(HttpClient teacher, Guid classId, string name, DateOnly heldOn)
    {
        var response = await teacher.PostAsJsonAsync("/api/exams", new
        {
            classId, name, heldOn, parts = new[] { new { name = "Paper", maxMark = 100m } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var exam = await Json(response);
        return (exam.GetProperty("id").GetGuid(), [.. exam.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())]);
    }

    private static async Task Enter(HttpClient teacher, (Guid Id, Guid[] Parts) exam, params (Guid Enrolment, decimal Mark)[] marks)
    {
        var saved = await teacher.PutAsJsonAsync($"/api/exams/{exam.Id}/marks", new
        {
            rows = marks.Select(m => new { enrolmentId = m.Enrolment, absent = false, marks = new Dictionary<Guid, decimal?> { [exam.Parts[0]] = m.Mark } }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [Fact]
    public async Task Exam_progress_shows_the_latest_exam_per_class_and_who_dropped_since_the_one_before()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client, "2027 A/L Maths");
        var amara = await AddStudent(teacher.Client, classId, "Amara");     // 80 to 55: dropped
        var bandula = await AddStudent(teacher.Client, classId, "Bandula"); // 60 to 58: steady
        var chamari = await AddStudent(teacher.Client, classId, "Chamari"); // 50 to 70: improved
        await AddStudent(teacher.Client, classId, "Dilani");                // not marked yet

        var first = await NewExam(teacher.Client, classId, "Term 1", Today.AddDays(-20));
        await Enter(teacher.Client, first, (amara, 80m), (bandula, 60m), (chamari, 50m));
        var second = await NewExam(teacher.Client, classId, "Term 2", Today.AddDays(-5));
        await Enter(teacher.Client, second, (amara, 55m), (bandula, 58m), (chamari, 70m));

        var progress = await Json(await teacher.Client.GetAsync("/api/exams/progress"));
        var card = Assert.Single(progress.GetProperty("classes").EnumerateArray());
        Assert.Equal("Term 2", card.GetProperty("examName").GetString());
        Assert.Equal(61m, card.GetProperty("averagePercent").GetDecimal());
        Assert.Equal(63.33m, card.GetProperty("previousAverage").GetDecimal());
        Assert.Equal(4, card.GetProperty("students").GetInt32());
        Assert.Equal(3, card.GetProperty("marked").GetInt32());
        Assert.False(card.GetProperty("published").GetBoolean());

        Assert.Equal(1, progress.GetProperty("droppedTotal").GetInt32());
        var drop = Assert.Single(progress.GetProperty("dropped").EnumerateArray());
        Assert.Equal(amara, drop.GetProperty("enrolmentId").GetGuid());
        Assert.Equal(80m, drop.GetProperty("previous").GetDecimal());
        Assert.Equal(55m, drop.GetProperty("latest").GetDecimal());
    }

    [Fact]
    public async Task Exam_progress_ignores_a_future_exam_and_has_no_drops_with_only_one_exam()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client, "Grade 10 Science");
        var amara = await AddStudent(teacher.Client, classId, "Amara");

        var only = await NewExam(teacher.Client, classId, "First test", Today.AddDays(-3));
        await Enter(teacher.Client, only, (amara, 40m));
        await NewExam(teacher.Client, classId, "Next month", Today.AddDays(20));

        var progress = await Json(await teacher.Client.GetAsync("/api/exams/progress"));
        var card = Assert.Single(progress.GetProperty("classes").EnumerateArray());
        Assert.Equal("First test", card.GetProperty("examName").GetString());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("previousAverage").ValueKind);
        Assert.Equal(0, progress.GetProperty("droppedTotal").GetInt32());

        var nothing = await factory.NewTeacherAsync("Sunil");
        var none = await Json(await nothing.Client.GetAsync("/api/exams/progress"));
        Assert.Equal(0, none.GetProperty("classes").GetArrayLength());
    }
}
