using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GuruBook.Modules.Exams;
using GuruBook.Modules.Exams.Services;
using GuruBook.Shared.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Api.Tests;

/// <summary>Grade schemes, exams with parts, marks entry, grades and ranks.</summary>
public class ExamsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 7000;

    private static string NewPhone() => $"075{Interlocked.Increment(ref _phoneCounter):D7}";

    private static readonly DateOnly HeldOn = new(2026, 10, 10);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

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

    private static async Task<Guid> AddStudent(HttpClient teacher, Guid classId, string first)
    {
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone = NewPhone(), firstName = first, lastName = "Silva", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
    }

    // An MCQ out of 40 and an essay out of 60: 100 marks, so a total is also the percentage.
    private static object Parts => new[] { new { name = "MCQ", maxMark = 40m }, new { name = "Essay", maxMark = 60m } };

    private static async Task<JsonElement> NewExam(HttpClient teacher, Guid classId, string name = "Term test")
    {
        var response = await teacher.PostAsJsonAsync("/api/exams", new { classId, name, heldOn = HeldOn, parts = Parts });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private static Guid[] PartIds(JsonElement exam) => [.. exam.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];

    private static object Row(Guid enrolment, Guid[] parts, decimal? mcq, decimal? essay, bool absent = false) =>
        new { enrolmentId = enrolment, absent, marks = new Dictionary<Guid, decimal?> { [parts[0]] = mcq, [parts[1]] = essay } };

    private static Task<HttpResponseMessage> Save(HttpClient teacher, Guid examId, params object[] rows) =>
        teacher.PutAsJsonAsync($"/api/exams/{examId}/marks", new { rows });

    private static JsonElement Student(JsonElement detail, Guid enrolment) =>
        detail.GetProperty("students").EnumerateArray().Single(s => s.GetProperty("enrolmentId").GetGuid() == enrolment);

    [Fact]
    public async Task The_grade_scheme_starts_as_the_ol_default_and_the_teacher_can_edit_it()
    {
        var teacher = await factory.NewTeacherAsync();
        var scheme = await Json(await teacher.Client.GetAsync("/api/exams/grade-scheme"));
        Assert.Equal(
            new[] { "A:75", "B:65", "C:50", "S:35", "W:0" },
            scheme.GetProperty("bands").EnumerateArray().Select(b => $"{b.GetProperty("grade").GetString()}:{b.GetProperty("minPercent").GetDecimal():0.##}"));

        var saved = await teacher.Client.PutAsJsonAsync("/api/exams/grade-scheme", new
        {
            bands = new[] { new { grade = "A", minPercent = 80m }, new { grade = "B", minPercent = 60m }, new { grade = "F", minPercent = 0m } },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var again = await Json(await teacher.Client.GetAsync("/api/exams/grade-scheme"));
        Assert.Equal(["A", "B", "F"], again.GetProperty("bands").EnumerateArray().Select(b => b.GetProperty("grade").GetString()));

        // Another teacher still has the default.
        var other = await factory.NewTeacherAsync("Sunil");
        var otherScheme = await Json(await other.Client.GetAsync("/api/exams/grade-scheme"));
        Assert.Equal(5, otherScheme.GetProperty("bands").GetArrayLength());
    }

    [Fact]
    public async Task A_grade_scheme_must_give_every_percentage_a_grade_and_have_no_repeats()
    {
        var teacher = await factory.NewTeacherAsync();
        async Task<string?> Try(params (string? Grade, decimal? Min)[] bands) =>
            await Code(await teacher.Client.PutAsJsonAsync("/api/exams/grade-scheme", new { bands = bands.Select(b => new { grade = b.Grade, minPercent = b.Min }) }));

        Assert.Equal("bands_invalid", await Try());
        Assert.Equal("band_zero_missing", await Try(("A", 75m), ("B", 40m)));
        Assert.Equal("grade_invalid", await Try(("", 0m)));
        Assert.Equal("grade_invalid", await Try(("ABCD", 0m)));
        Assert.Equal("grade_duplicate", await Try(("A", 50m), ("a", 0m)));
        Assert.Equal("min_percent_duplicate", await Try(("A", 0m), ("B", 0m)));
        Assert.Equal("min_percent_invalid", await Try(("A", 101m), ("B", 0m)));
        Assert.Equal("min_percent_invalid", await Try(("A", null), ("B", 0m)));
        Assert.Null(await Try(("P", 35m), ("F", 0m)));
    }

    [Fact]
    public async Task An_exam_is_made_with_parts_and_checked_for_the_usual_mistakes()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var exam = await NewExam(teacher.Client, classId);
        Assert.Equal(100m, exam.GetProperty("maxTotal").GetDecimal());
        Assert.Equal(["MCQ", "Essay"], exam.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("2027 A/L Maths", exam.GetProperty("classTitle").GetString());

        async Task<string?> Try(string? name, object? parts, Guid? cls = null, DateOnly? on = null) =>
            await Code(await teacher.Client.PostAsJsonAsync("/api/exams", new { classId = cls ?? classId, name, heldOn = on ?? HeldOn, parts }));

        Assert.Equal("name_required", await Try(" ", Parts));
        Assert.Equal("name_too_long", await Try(new string('x', 101), Parts));
        Assert.Equal("parts_invalid", await Try("T", Array.Empty<object>()));
        Assert.Equal("part_name_required", await Try("T", new[] { new { name = "", maxMark = 10m } }));
        Assert.Equal("part_max_invalid", await Try("T", new[] { new { name = "P", maxMark = 0m } }));
        Assert.Equal("part_max_invalid", await Try("T", new[] { new { name = "P", maxMark = 10.555m } }));
        Assert.Equal("part_name_duplicate", await Try("T", new[] { new { name = "P", maxMark = 10m }, new { name = "p", maxMark = 20m } }));
        Assert.Equal("class_not_found", await Try("T", Parts, Guid.NewGuid()));
        var noDate = await teacher.Client.PostAsJsonAsync("/api/exams", new { classId, name = "T", parts = Parts });
        Assert.Equal("held_on_required", await Code(noDate));

        var other = await factory.NewTeacherAsync("Sunil");
        var foreign = await other.Client.PostAsJsonAsync("/api/exams", new { classId, name = "T", heldOn = HeldOn, parts = Parts });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Marks_give_totals_percentages_and_grades_and_absent_is_not_zero()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await AddStudent(teacher.Client, classId, "Amara");
        var bandula = await AddStudent(teacher.Client, classId, "Bandula");
        var chamari = await AddStudent(teacher.Client, classId, "Chamari");
        var dilshan = await AddStudent(teacher.Client, classId, "Dilshan");
        var exam = await NewExam(teacher.Client, classId);
        var examId = exam.GetProperty("id").GetGuid();
        var parts = PartIds(exam);

        var saved = await Json(await Save(teacher.Client, examId,
            Row(amara, parts, 35m, 50m),        // 85 -> A
            Row(bandula, parts, 20m, 30.5m),    // 50.5 -> C
            Row(chamari, parts, 0m, 0m),        // really scored 0 -> W, ranked last
            Row(dilshan, parts, null, null, absent: true)));

        var a = Student(saved, amara);
        Assert.Equal(85m, a.GetProperty("total").GetDecimal());
        Assert.Equal(85m, a.GetProperty("percent").GetDecimal());
        Assert.Equal("A", a.GetProperty("grade").GetString());
        Assert.Equal(1, a.GetProperty("rank").GetInt32());

        var b = Student(saved, bandula);
        Assert.Equal(50.5m, b.GetProperty("total").GetDecimal());
        Assert.Equal("C", b.GetProperty("grade").GetString());
        Assert.Equal(2, b.GetProperty("rank").GetInt32());

        var c = Student(saved, chamari);
        Assert.Equal(0m, c.GetProperty("total").GetDecimal());
        Assert.Equal("W", c.GetProperty("grade").GetString());
        Assert.Equal(3, c.GetProperty("rank").GetInt32());

        var d = Student(saved, dilshan);
        Assert.True(d.GetProperty("absent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("total").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("grade").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("rank").ValueKind);

        var summary = saved.GetProperty("summary");
        Assert.Equal(4, summary.GetProperty("students").GetInt32());
        Assert.Equal(4, summary.GetProperty("marked").GetInt32());
        Assert.Equal(45.17m, summary.GetProperty("averagePercent").GetDecimal());

        // Reading it again gives the same answer from the saved results.
        var read = await Json(await teacher.Client.GetAsync($"/api/exams/{examId}"));
        Assert.Equal(3, Student(read, chamari).GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task Equal_totals_share_a_rank_the_next_is_skipped_and_unfinished_students_are_unranked()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var names = new[] { "Amara", "Bandula", "Chamari", "Dilshan", "Eranga", "Fathima" };
        var ids = new List<Guid>();
        foreach (var name in names)
        {
            ids.Add(await AddStudent(teacher.Client, classId, name));
        }

        var exam = await NewExam(teacher.Client, classId);
        var examId = exam.GetProperty("id").GetGuid();
        var parts = PartIds(exam);

        // 90, 70, 70, 50 across the first four; Eranga has only the MCQ; Fathima has nothing.
        var saved = await Json(await Save(teacher.Client, examId,
            Row(ids[0], parts, 40m, 50m),
            Row(ids[1], parts, 30m, 40m),
            Row(ids[2], parts, 10m, 60m),
            Row(ids[3], parts, 20m, 30m),
            Row(ids[4], parts, 38m, null),
            Row(ids[5], parts, null, null)));

        Assert.Equal([1, 2, 2, 4], ids.Take(4).Select(id => Student(saved, id).GetProperty("rank").GetInt32()));
        var partial = Student(saved, ids[4]);
        Assert.Equal(38m, partial.GetProperty("total").GetDecimal());
        Assert.Equal(JsonValueKind.Null, partial.GetProperty("rank").ValueKind);
        Assert.Equal(JsonValueKind.Null, partial.GetProperty("grade").ValueKind);
        Assert.Equal(JsonValueKind.Null, Student(saved, ids[5]).GetProperty("total").ValueKind);
        Assert.Equal(4, saved.GetProperty("summary").GetProperty("marked").GetInt32());

        // Finishing Eranga's essay moves the ranks: 98 beats everyone.
        var more = await Json(await Save(teacher.Client, examId, Row(ids[4], parts, 38m, 60m)));
        Assert.Equal(1, Student(more, ids[4]).GetProperty("rank").GetInt32());
        Assert.Equal(2, Student(more, ids[0]).GetProperty("rank").GetInt32());
        Assert.Equal(5, Student(more, ids[3]).GetProperty("rank").GetInt32());

        // Marking someone absent who had marks takes them out of the ranking; clearing a row removes the marks.
        var absent = await Json(await Save(teacher.Client, examId, Row(ids[4], parts, null, null, absent: true), Row(ids[3], parts, null, null)));
        Assert.Equal(JsonValueKind.Null, Student(absent, ids[4]).GetProperty("rank").ValueKind);
        Assert.Equal(JsonValueKind.Null, Student(absent, ids[3]).GetProperty("total").ValueKind);
        Assert.Equal([1, 2, 2], ids.Take(3).Select(id => Student(absent, id).GetProperty("rank").GetInt32()));
    }

    [Fact]
    public async Task Marks_are_checked_against_the_parts_and_the_class()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await AddStudent(teacher.Client, classId, "Amara");
        var exam = await NewExam(teacher.Client, classId);
        var examId = exam.GetProperty("id").GetGuid();
        var parts = PartIds(exam);

        Assert.Equal("mark_invalid", await Code(await Save(teacher.Client, examId, Row(amara, parts, 41m, 10m))));
        Assert.Equal("mark_invalid", await Code(await Save(teacher.Client, examId, Row(amara, parts, -1m, 10m))));
        Assert.Equal("mark_invalid", await Code(await Save(teacher.Client, examId, Row(amara, parts, 10.255m, 10m))));
        Assert.Equal("part_not_found", await Code(await Save(teacher.Client, examId,
            new { enrolmentId = amara, absent = false, marks = new Dictionary<Guid, decimal?> { [Guid.NewGuid()] = 5m } })));
        Assert.Equal("student_not_in_class", await Code(await Save(teacher.Client, examId, Row(Guid.NewGuid(), parts, 1m, 1m))));
        Assert.Equal("marks_invalid", await Code(await Save(teacher.Client, examId)));
        Assert.Equal(HttpStatusCode.NotFound, (await Save(teacher.Client, Guid.NewGuid(), Row(amara, parts, 1m, 1m))).StatusCode);

        // A student of another class of the same teacher cannot be marked in this exam.
        var otherClass = await NewClass(teacher.Client, "2028 O/L Science");
        var outsider = await AddStudent(teacher.Client, otherClass, "Outsider");
        Assert.Equal("student_not_in_class", await Code(await Save(teacher.Client, examId, Row(outsider, parts, 1m, 1m))));

        // Nothing was saved by the rejected requests.
        var read = await Json(await teacher.Client.GetAsync($"/api/exams/{examId}"));
        Assert.Equal(JsonValueKind.Null, Student(read, amara).GetProperty("total").ValueKind);
    }

    [Fact]
    public async Task Editing_an_exam_keeps_marks_safe_and_recalculates_when_a_maximum_changes()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await AddStudent(teacher.Client, classId, "Amara");
        var bandula = await AddStudent(teacher.Client, classId, "Bandula");
        var exam = await NewExam(teacher.Client, classId);
        var examId = exam.GetProperty("id").GetGuid();
        var parts = PartIds(exam);

        // Before any marks the parts can be changed freely.
        var free = await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}", new
        {
            name = "Mid-term", heldOn = HeldOn, parts = new[] { new { name = "Paper", maxMark = 50m } },
        });
        Assert.Equal(HttpStatusCode.OK, free.StatusCode);
        var single = await Json(free);
        Assert.Equal("Mid-term", single.GetProperty("name").GetString());
        Assert.Equal(50m, single.GetProperty("maxTotal").GetDecimal());
        parts = PartIds(single);
        Assert.Single(parts);

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}/marks", new
        {
            rows = new object[]
            {
                new { enrolmentId = amara, absent = false, marks = new Dictionary<Guid, decimal?> { [parts[0]] = 45m } },
                new { enrolmentId = bandula, absent = false, marks = new Dictionary<Guid, decimal?> { [parts[0]] = 20m } },
            },
        })).StatusCode);

        object Edit(string name, params (Guid? Id, string Name, decimal Max)[] list) =>
            new { name, heldOn = HeldOn, parts = list.Select(p => new { id = p.Id, name = p.Name, maxMark = p.Max }) };

        // With marks in, parts are matched by id: no adding, and no maximum below a mark already entered.
        Assert.Equal("parts_locked", await Code(await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}", Edit("Mid-term", (null, "New", 10m)))));
        Assert.Equal("parts_locked", await Code(await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}", Edit("Mid-term", (parts[0], "Paper", 50m), (null, "Extra", 10m)))));
        Assert.Equal("max_below_marks", await Code(await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}", Edit("Mid-term", (parts[0], "Paper", 40m)))));

        // A new maximum moves every percentage and grade: 45 of 90 is now 50%.
        var changed = await Json(await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}", Edit("Mid-term test", (parts[0], "Paper 1", 90m))));
        Assert.Equal("Mid-term test", changed.GetProperty("name").GetString());
        Assert.Equal("Paper 1", changed.GetProperty("parts")[0].GetProperty("name").GetString());
        Assert.Equal(50m, Student(changed, amara).GetProperty("percent").GetDecimal());
        Assert.Equal("C", Student(changed, amara).GetProperty("grade").GetString());
        Assert.Equal(1, Student(changed, amara).GetProperty("rank").GetInt32());

        // Changing the grade scheme changes the grade shown, with no change to the marks.
        await teacher.Client.PutAsJsonAsync("/api/exams/grade-scheme", new { bands = new[] { new { grade = "P", minPercent = 40m }, new { grade = "F", minPercent = 0m } } });
        var regraded = await Json(await teacher.Client.GetAsync($"/api/exams/{examId}"));
        Assert.Equal("P", Student(regraded, amara).GetProperty("grade").GetString());
        Assert.Equal("F", Student(regraded, bandula).GetProperty("grade").GetString());
    }

    [Fact]
    public async Task Exams_list_newest_first_per_class_and_can_be_deleted_with_their_marks()
    {
        var teacher = await factory.NewTeacherAsync();
        var maths = await NewClass(teacher.Client);
        var science = await NewClass(teacher.Client, "2028 O/L Science");
        var amara = await AddStudent(teacher.Client, maths, "Amara");
        await AddStudent(teacher.Client, maths, "Bandula");

        var older = await Json(await teacher.Client.PostAsJsonAsync("/api/exams", new { classId = maths, name = "Unit 1", heldOn = new DateOnly(2026, 9, 1), parts = Parts }));
        var newer = await Json(await teacher.Client.PostAsJsonAsync("/api/exams", new { classId = maths, name = "Unit 2", heldOn = new DateOnly(2026, 10, 1), parts = Parts }));
        await NewExam(teacher.Client, science, "Science quiz");
        await Save(teacher.Client, newer.GetProperty("id").GetGuid(), Row(amara, PartIds(newer), 40m, 30m));

        var all = await Json(await teacher.Client.GetAsync("/api/exams"));
        Assert.Equal(["Science quiz", "Unit 2", "Unit 1"], all.EnumerateArray().Select(e => e.GetProperty("name").GetString()));
        var forMaths = await Json(await teacher.Client.GetAsync($"/api/exams?classId={maths}"));
        Assert.Equal(["Unit 2", "Unit 1"], forMaths.EnumerateArray().Select(e => e.GetProperty("name").GetString()));
        var latest = (await Json(await teacher.Client.GetAsync($"/api/exams?classId={maths}&limit=1"))).EnumerateArray().Single();
        Assert.Equal("Unit 2", latest.GetProperty("name").GetString());
        Assert.Equal(2, latest.GetProperty("students").GetInt32());
        Assert.Equal(1, latest.GetProperty("marked").GetInt32());
        Assert.Equal(70m, latest.GetProperty("averagePercent").GetDecimal());
        Assert.Equal(100m, latest.GetProperty("maxTotal").GetDecimal());

        var other = await factory.NewTeacherAsync("Sunil");
        Assert.Empty((await Json(await other.Client.GetAsync("/api/exams"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/exams/{newer.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync($"/api/exams/{newer.GetProperty("id").GetGuid()}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/exams/{newer.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.Client.GetAsync($"/api/exams/{newer.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(2, (await Json(await teacher.Client.GetAsync("/api/exams"))).GetArrayLength());
        Assert.NotEqual(Guid.Empty, older.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_student_who_left_keeps_their_marks_but_cannot_be_given_new_ones()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await AddStudent(teacher.Client, classId, "Amara");
        var bandula = await AddStudent(teacher.Client, classId, "Bandula");
        var exam = await NewExam(teacher.Client, classId);
        var examId = exam.GetProperty("id").GetGuid();
        var parts = PartIds(exam);
        await Save(teacher.Client, examId, Row(amara, parts, 30m, 30m));

        var left = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{amara}", new { status = "Left" });
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{bandula}", new { status = "Left" });

        var read = await Json(await teacher.Client.GetAsync($"/api/exams/{examId}"));
        Assert.Equal(60m, Student(read, amara).GetProperty("total").GetDecimal());
        Assert.False(Student(read, amara).GetProperty("isActive").GetBoolean());
        Assert.DoesNotContain(read.GetProperty("students").EnumerateArray(), s => s.GetProperty("enrolmentId").GetGuid() == bandula);
        Assert.Equal("student_not_in_class", await Code(await Save(teacher.Client, examId, Row(bandula, parts, 5m, 5m))));
        Assert.Equal(HttpStatusCode.OK, (await Save(teacher.Client, examId, Row(amara, parts, 35m, 30m))).StatusCode);
    }

    [Fact]
    public async Task Every_mark_and_every_change_to_it_is_logged_with_the_teacher_who_made_it()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await AddStudent(teacher.Client, classId, "Amara");
        var exam = await NewExam(teacher.Client, classId);
        var examId = exam.GetProperty("id").GetGuid();
        var parts = PartIds(exam);

        await Save(teacher.Client, examId, Row(amara, parts, 35m, 20m));
        await Save(teacher.Client, examId, Row(amara, parts, 36m, 20m));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExamsDbContext>();
        var all = await db.Set<AuditLogEntry>().Where(e => e.Entity.Contains("ExamPartMark")).ToListAsync();
        var markId = all.Single(e => e.Action == AuditAction.Created && e.After!.Contains(parts[0].ToString())).EntityId;
        var entries = all.Where(e => e.EntityId == markId).ToList();
        var update = Assert.Single(entries, e => e.Action == AuditAction.Updated);
        Assert.Contains("\"Mark\":35", update.Before);
        Assert.Contains("\"Mark\":36", update.After);
        Assert.NotNull(update.UserId);
    }

    [Fact]
    public async Task Exams_need_a_signed_in_teacher()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/exams")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/exams/grade-scheme")).StatusCode);
    }
}

public class ExamCalculatorTests
{
    [Theory]
    [InlineData(75, "A")]
    [InlineData(74.99, "B")]
    [InlineData(65, "B")]
    [InlineData(50, "C")]
    [InlineData(49.99, "S")]
    [InlineData(35, "S")]
    [InlineData(34.99, "W")]
    [InlineData(0, "W")]
    [InlineData(100, "A")]
    public void The_ol_default_gives_the_grade_of_the_highest_band_reached(double percent, string grade)
    {
        Assert.Equal(grade, ExamCalculator.GradeFor((decimal)percent, ExamCalculator.OlDefault));
    }

    [Fact]
    public void A_percentage_below_every_band_earns_no_grade()
    {
        Assert.Null(ExamCalculator.GradeFor(10m, [new BandRule("A", 50m)]));
    }

    [Fact]
    public void Percent_is_rounded_to_two_places_and_empty_exams_have_none()
    {
        Assert.Equal(66.67m, ExamCalculator.Percent(2m, 3m));
        Assert.Equal(12.5m, ExamCalculator.Percent(5m, 40m));
        Assert.Null(ExamCalculator.Percent(5m, 0m));
    }

    [Fact]
    public void Ranks_share_a_place_on_equal_totals_and_skip_the_next()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var ranks = ExamCalculator.Rank(
        [
            (ids[0], 90m), (ids[1], 70m), (ids[2], 70m), (ids[3], 50m), (ids[4], 50m), (ids[5], 10m),
        ]);
        Assert.Equal([1, 2, 2, 4, 4, 6], ids.Select(id => ranks[id]));
        Assert.Empty(ExamCalculator.Rank([]));
    }
}
