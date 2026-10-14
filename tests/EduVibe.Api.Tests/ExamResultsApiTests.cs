using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduVibe.Modules.Exams.Services;

namespace EduVibe.Api.Tests;

/// <summary>Exam analysis, a student's trend across exams, publishing results to students, and the results Excel file.</summary>
public class ExamResultsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 3000;

    private static string NewPhone() => $"078{Interlocked.Increment(ref _phoneCounter):D7}";

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

    private static async Task<(Guid Enrolment, string Phone, string Otp)> NewStudent(HttpClient teacher, Guid classId, string first)
    {
        var phone = NewPhone();
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Silva", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        return (body.GetProperty("row").GetProperty("enrolmentId").GetGuid(), phone, body.GetProperty("oneTimePassword").GetString()!);
    }

    private static async Task<Guid> AddStudent(HttpClient teacher, Guid classId, string first) => (await NewStudent(teacher, classId, first)).Enrolment;

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

    // An MCQ out of 40 and an essay out of 60: 100 marks, so a total is also the percentage.
    private static async Task<(Guid Id, Guid[] Parts)> NewExam(HttpClient teacher, Guid classId, string name, DateOnly heldOn)
    {
        var response = await teacher.PostAsJsonAsync("/api/exams", new
        {
            classId, name, heldOn, parts = new[] { new { name = "MCQ", maxMark = 40m }, new { name = "Essay", maxMark = 60m } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var exam = await Json(response);
        return (exam.GetProperty("id").GetGuid(), [.. exam.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())]);
    }

    private static object Row(Guid enrolment, Guid[] parts, decimal? mcq, decimal? essay, bool absent = false) =>
        new { enrolmentId = enrolment, absent, marks = new Dictionary<Guid, decimal?> { [parts[0]] = mcq, [parts[1]] = essay } };

    private static async Task<JsonElement> Save(HttpClient teacher, Guid examId, params object[] rows)
    {
        var response = await teacher.PutAsJsonAsync($"/api/exams/{examId}/marks", new { rows });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static decimal? Number(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetDecimal();

    [Fact]
    public async Task The_analysis_gives_highest_lowest_average_median_pass_rate_grades_and_part_averages()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var ids = new List<Guid>();
        foreach (var name in new[] { "Amara", "Bandula", "Chamari", "Dilshan", "Eranga", "Fathima" })
        {
            ids.Add(await AddStudent(teacher.Client, classId, name));
        }

        var (examId, parts) = await NewExam(teacher.Client, classId, "Term test", new DateOnly(2026, 10, 10));
        var saved = await Save(teacher.Client, examId,
            Row(ids[0], parts, 40m, 50m),       // 90 -> A
            Row(ids[1], parts, 30m, 40m),       // 70 -> B
            Row(ids[2], parts, 20m, 50m),       // 70 -> B
            Row(ids[3], parts, 10m, 20m),       // 30 -> W, fails
            Row(ids[4], parts, null, null, absent: true),
            Row(ids[5], parts, 20m, null));     // not finished: left out of everything but the MCQ average

        var analysis = saved.GetProperty("analysis");
        Assert.Equal(4, analysis.GetProperty("sat").GetInt32());
        Assert.Equal(1, analysis.GetProperty("absent").GetInt32());
        Assert.Equal(90m, Number(analysis, "highest"));
        Assert.Equal(30m, Number(analysis, "lowest"));
        Assert.Equal(65m, Number(analysis, "average"));
        Assert.Equal(70m, Number(analysis, "median"));
        Assert.Equal(35m, Number(analysis, "passMark"));
        Assert.Equal(3, analysis.GetProperty("passed").GetInt32());
        Assert.Equal(75, analysis.GetProperty("passRate").GetInt32());
        Assert.Equal(
            ["A:1", "B:2", "C:0", "S:0", "W:1"],
            analysis.GetProperty("grades").EnumerateArray().Select(g => $"{g.GetProperty("grade").GetString()}:{g.GetProperty("count").GetInt32()}"));

        var partAverages = analysis.GetProperty("parts").EnumerateArray().ToList();
        Assert.Equal(["MCQ", "Essay"], partAverages.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(24m, Number(partAverages[0], "average"));  // (40 + 30 + 20 + 10 + 20) / 5
        Assert.Equal(40m, Number(partAverages[1], "average"));  // (50 + 40 + 50 + 20) / 4

        // An exam with nothing entered has an empty analysis rather than zeros.
        var (emptyId, _) = await NewExam(teacher.Client, classId, "Unit 2", new DateOnly(2026, 10, 12));
        var empty = (await Json(await teacher.Client.GetAsync($"/api/exams/{emptyId}"))).GetProperty("analysis");
        Assert.Equal(0, empty.GetProperty("sat").GetInt32());
        Assert.Null(Number(empty, "highest"));
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("passRate").ValueKind);
    }

    [Fact]
    public async Task A_students_history_lists_their_exams_oldest_first_with_rank_and_the_class_average()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await AddStudent(teacher.Client, classId, "Amara");
        var bandula = await AddStudent(teacher.Client, classId, "Bandula");

        var (second, p2) = await NewExam(teacher.Client, classId, "Unit 2", new DateOnly(2026, 10, 1));
        var (first, p1) = await NewExam(teacher.Client, classId, "Unit 1", new DateOnly(2026, 9, 1));
        var (third, p3) = await NewExam(teacher.Client, classId, "Unit 3", new DateOnly(2026, 10, 20));
        await Save(teacher.Client, first, Row(amara, p1, 30m, 30m), Row(bandula, p1, 40m, 40m));    // Amara 60, 2nd of 2
        await Save(teacher.Client, second, Row(amara, p2, 40m, 45m), Row(bandula, p2, 30m, 30m));   // Amara 85, 1st of 2
        await Save(teacher.Client, third, Row(bandula, p3, 10m, 10m));                              // nothing for Amara: left out

        var history = await Json(await teacher.Client.GetAsync($"/api/exams/enrolments/{amara}/history"));
        Assert.Equal("Amara Silva", history.GetProperty("fullName").GetString());
        Assert.Equal("2027 A/L Maths", history.GetProperty("classTitle").GetString());
        var exams = history.GetProperty("exams").EnumerateArray().ToList();
        Assert.Equal(["Unit 1", "Unit 2"], exams.Select(e => e.GetProperty("name").GetString()));
        Assert.Equal(60m, Number(exams[0], "percent"));
        Assert.Equal("C", exams[0].GetProperty("grade").GetString());
        Assert.Equal(2, exams[0].GetProperty("rank").GetInt32());
        Assert.Equal(2, exams[0].GetProperty("ranked").GetInt32());
        Assert.Equal(70m, Number(exams[0], "classAverage"));
        Assert.Equal("A", exams[1].GetProperty("grade").GetString());
        Assert.Equal(1, exams[1].GetProperty("rank").GetInt32());

        // Absent shows as absent, with no mark and no rank.
        await Save(teacher.Client, second, Row(amara, p2, null, null, absent: true));
        var again = (await Json(await teacher.Client.GetAsync($"/api/exams/enrolments/{amara}/history"))).GetProperty("exams")[1];
        Assert.True(again.GetProperty("absent").GetBoolean());
        Assert.Null(Number(again, "total"));
        Assert.Equal(JsonValueKind.Null, again.GetProperty("rank").ValueKind);

        // The student's other classes with this teacher are offered for switching.
        var physics = await NewClass(teacher.Client, "2027 A/L Physics");
        var enrol = await teacher.Client.PostAsJsonAsync($"/api/students/{history.GetProperty("studentId").GetGuid()}/enrolments", new { classId = physics, feePlan = "Full" });
        Assert.True(enrol.IsSuccessStatusCode, await enrol.Content.ReadAsStringAsync());
        var withPhysics = await Json(await teacher.Client.GetAsync($"/api/exams/enrolments/{amara}/history"));
        Assert.Equal(2, withPhysics.GetProperty("classes").GetArrayLength());

        var other = await factory.NewTeacherAsync("Sunil");
        var foreign = await other.Client.GetAsync($"/api/exams/enrolments/{amara}/history");
        Assert.Equal("enrolment_not_found", await Code(foreign));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Students_see_only_their_own_results_and_only_once_the_teacher_publishes()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await NewStudent(teacher.Client, classId, "Amara");
        var bandula = await AddStudent(teacher.Client, classId, "Bandula");
        var chamari = await AddStudent(teacher.Client, classId, "Chamari");
        var (examId, parts) = await NewExam(teacher.Client, classId, "Term test", new DateOnly(2026, 10, 10));
        await Save(teacher.Client, examId,
            Row(amara.Enrolment, parts, 30m, 40m),    // 70 -> B, 2nd
            Row(bandula, parts, 35m, 50m),            // 85 -> A, 1st
            Row(chamari, parts, null, null, absent: true));

        var student = await SignInStudent(amara.Phone, amara.Otp);
        Assert.Empty((await Json(await student.GetAsync("/api/exams/mine"))).EnumerateArray());

        var published = await teacher.Client.PostAsync($"/api/exams/{examId}/publish", null);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var publishedAt = (await Json(published)).GetProperty("publishedAt").GetDateTimeOffset();

        var mine = Assert.Single((await Json(await student.GetAsync("/api/exams/mine"))).EnumerateArray());
        Assert.Equal("Term test", mine.GetProperty("name").GetString());
        Assert.Equal(classId, mine.GetProperty("classId").GetGuid());
        Assert.Equal(70m, Number(mine, "total"));
        Assert.Equal(100m, Number(mine, "maxTotal"));
        Assert.Equal("B", mine.GetProperty("grade").GetString());
        Assert.Equal(2, mine.GetProperty("rank").GetInt32());
        Assert.Equal(2, mine.GetProperty("ranked").GetInt32());
        Assert.Equal(
            ["MCQ:30", "Essay:40"],
            mine.GetProperty("parts").EnumerateArray().Select(p => $"{p.GetProperty("name").GetString()}:{p.GetProperty("mark").GetDecimal():0.##}"));
        // Nothing about the other students is sent.
        Assert.DoesNotContain("Bandula", mine.GetRawText());
        Assert.DoesNotContain("85", mine.GetRawText());

        // Publishing again keeps the first date; marks changed after publishing reach the student.
        var again = await Json(await teacher.Client.PostAsync($"/api/exams/{examId}/publish", null));
        Assert.Equal(publishedAt, again.GetProperty("publishedAt").GetDateTimeOffset());
        await Save(teacher.Client, examId, Row(amara.Enrolment, parts, 40m, 50m));
        Assert.Equal(90m, Number((await Json(await student.GetAsync("/api/exams/mine")))[0], "total"));

        var list = (await Json(await teacher.Client.GetAsync($"/api/exams?classId={classId}")))[0];
        Assert.NotEqual(JsonValueKind.Null, list.GetProperty("publishedAt").ValueKind);

        var hidden = await Json(await teacher.Client.PostAsync($"/api/exams/{examId}/unpublish", null));
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("publishedAt").ValueKind);
        Assert.Empty((await Json(await student.GetAsync("/api/exams/mine"))).EnumerateArray());

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/exams/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"/api/exams/{examId}/publish", null)).StatusCode);
        var other = await factory.NewTeacherAsync("Sunil");
        Assert.Equal("exam_not_found", await Code(await other.Client.PostAsync($"/api/exams/{examId}/publish", null)));
    }

    [Fact]
    public async Task An_absent_student_sees_absent_and_an_unfinished_one_sees_no_grade()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await NewStudent(teacher.Client, classId, "Amara");
        var (absentExam, p1) = await NewExam(teacher.Client, classId, "Unit 1", new DateOnly(2026, 9, 1));
        var (partExam, p2) = await NewExam(teacher.Client, classId, "Unit 2", new DateOnly(2026, 10, 1));
        await Save(teacher.Client, absentExam, Row(amara.Enrolment, p1, null, null, absent: true));
        await Save(teacher.Client, partExam, Row(amara.Enrolment, p2, 25m, null));
        await teacher.Client.PostAsync($"/api/exams/{absentExam}/publish", null);
        await teacher.Client.PostAsync($"/api/exams/{partExam}/publish", null);

        var student = await SignInStudent(amara.Phone, amara.Otp);
        var mine = (await Json(await student.GetAsync("/api/exams/mine"))).EnumerateArray().ToList();
        Assert.Equal(["Unit 2", "Unit 1"], mine.Select(m => m.GetProperty("name").GetString()));
        Assert.Equal(25m, Number(mine[0], "total"));
        Assert.Equal(JsonValueKind.Null, mine[0].GetProperty("grade").ValueKind);
        Assert.True(mine[1].GetProperty("absent").GetBoolean());
        Assert.Null(Number(mine[1], "total"));
        Assert.All(mine[1].GetProperty("parts").EnumerateArray(), p => Assert.Equal(JsonValueKind.Null, p.GetProperty("mark").ValueKind));
    }

    [Fact]
    public async Task The_results_report_is_an_excel_file_with_every_part_total_grade_and_rank()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var amara = await AddStudent(teacher.Client, classId, "Amara");
        var bandula = await AddStudent(teacher.Client, classId, "Bandula");
        var (examId, parts) = await NewExam(teacher.Client, classId, "Term test", new DateOnly(2026, 10, 10));
        await Save(teacher.Client, examId, Row(amara, parts, 37.5m, 50m), Row(bandula, parts, null, null, absent: true));

        var response = await teacher.Client.GetAsync($"/api/reports/exam-results?examId={examId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sheet = SheetXml(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("MCQ (/40)", sheet);
        Assert.Contains("Total (/100)", sheet);
        Assert.Contains("Amara Silva", sheet);
        Assert.Contains("<v>87.5</v>", sheet);
        Assert.Contains("Absent", sheet);

        var other = await factory.NewTeacherAsync("Sunil");
        var foreign = await other.Client.GetAsync($"/api/reports/exam-results?examId={examId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("exam_not_found", await Code(foreign));
        Assert.Equal("exam_not_found", await Code(await teacher.Client.GetAsync("/api/reports/exam-results")));
    }

    private static string SheetXml(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return reader.ReadToEnd();
    }
}

public class ExamStatsTests
{
    [Fact]
    public void The_median_is_the_middle_value_or_the_average_of_the_two_middle_values()
    {
        Assert.Equal(5m, ExamCalculator.Median([9m, 1m, 5m]));
        Assert.Equal(4m, ExamCalculator.Median([1m, 3m, 5m, 9m]));
        Assert.Equal(7.5m, ExamCalculator.Median([10m, 5m]));
        Assert.Null(ExamCalculator.Median([]));
    }

    [Fact]
    public void Only_the_lowest_grade_fails()
    {
        Assert.Equal(35m, ExamCalculator.PassMark(ExamCalculator.OlDefault));
        Assert.Equal(40m, ExamCalculator.PassMark([new BandRule("P", 40m), new BandRule("F", 0m)]));
        Assert.Null(ExamCalculator.PassMark([new BandRule("P", 0m)]));
    }

    [Fact]
    public void Stats_count_grades_best_first_and_everyone_passes_with_a_single_grade()
    {
        var stats = ExamCalculator.Stats([(80m, 80m), (40m, 40m), (20m, 20m)], ExamCalculator.OlDefault);
        Assert.Equal(2, stats.Passed);
        Assert.Equal(67, stats.PassRate);
        Assert.Equal(46.67m, stats.Average);
        Assert.Equal(["A", "B", "C", "S", "W"], stats.Grades.Select(g => g.Grade));
        Assert.Equal([1, 0, 0, 1, 1], stats.Grades.Select(g => g.Count));

        var single = ExamCalculator.Stats([(10m, 10m)], [new BandRule("P", 0m)]);
        Assert.Equal(100, single.PassRate);
        Assert.Null(single.PassMark);
    }
}
