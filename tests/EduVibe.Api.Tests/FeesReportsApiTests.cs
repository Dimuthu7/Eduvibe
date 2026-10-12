using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduVibe.Modules.Reports;
using EduVibe.Shared.Modules;

namespace EduVibe.Api.Tests;

/// <summary>Fee totals across classes, "attended but unpaid", and the Excel reports.</summary>
public class FeesReportsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 8000;

    private static string NewPhone() => $"074{Interlocked.Increment(ref _phoneCounter):D7}";

    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    private static string ThisMonth => Today.ToString("yyyy-MM");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    // Meets every day of the week, so today always has a session to take attendance for.
    private async Task<Guid> DailyClass(HttpClient teacher, string title, decimal fee)
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home class", district = "Kandy" });
        var venueId = (await Json(venue)).GetProperty("id").GetGuid();
        var slots = Enumerable.Range(1, 7).Select(day => new { day, start = "16:00", end = "18:00" }).ToArray();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = fee, slots,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddStudent(HttpClient teacher, Guid classId, string first, string plan = "Full")
    {
        var response = await teacher.PostAsJsonAsync("/api/students", new
        {
            phone = NewPhone(), firstName = first, lastName = "Silva", classId, feePlan = plan,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
    }

    private static Task<HttpResponseMessage> Pay(HttpClient teacher, Guid enrolment, decimal amount) =>
        teacher.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = enrolment, amount, method = "Cash" });

    private static async Task TakeAttendance(HttpClient teacher, Guid classId, params (Guid Enrolment, string Status)[] marks)
    {
        var sessions = await Json(await teacher.GetAsync($"/api/attendance/sessions?date={Today:yyyy-MM-dd}"));
        var session = sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
        var saved = await teacher.PutAsJsonAsync($"/api/attendance/sessions/{session}/marks", new
        {
            marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private static string SheetXml(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task The_overview_adds_up_every_class_of_the_teacher_and_nobody_elses()
    {
        var teacher = await factory.NewTeacherAsync();
        var maths = await DailyClass(teacher.Client, "Maths", 2000m);
        var science = await DailyClass(teacher.Client, "Science", 1000m);
        var a = await AddStudent(teacher.Client, maths, "Aruni");
        await AddStudent(teacher.Client, maths, "Bimal");
        await AddStudent(teacher.Client, science, "Chathu");
        await AddStudent(teacher.Client, science, "Dilani", "FreeCard");
        await Pay(teacher.Client, a, 2000m);

        var other = await factory.NewTeacherAsync();
        var foreign = await DailyClass(other.Client, "Foreign", 5000m);
        await AddStudent(other.Client, foreign, "Eranga");

        var overview = await Json(await teacher.Client.GetAsync("/api/fees/overview"));
        Assert.Equal(ThisMonth, overview.GetProperty("month").GetString());
        var totals = overview.GetProperty("totals");
        Assert.Equal(5000m, totals.GetProperty("due").GetDecimal());
        Assert.Equal(2000m, totals.GetProperty("collected").GetDecimal());
        Assert.Equal(3000m, totals.GetProperty("outstanding").GetDecimal());
        Assert.Equal(1, totals.GetProperty("paid").GetInt32());
        Assert.Equal(2, totals.GetProperty("unpaid").GetInt32());
        Assert.Equal(1, totals.GetProperty("freeCard").GetInt32());

        var classes = overview.GetProperty("classes").EnumerateArray().ToList();
        Assert.Equal(["Maths", "Science"], classes.Select(c => c.GetProperty("title").GetString()!).Order().ToArray());
        var mathsRow = classes.Single(c => c.GetProperty("classId").GetGuid() == maths).GetProperty("totals");
        Assert.Equal(2000m, mathsRow.GetProperty("outstanding").GetDecimal());
        Assert.DoesNotContain(classes, c => c.GetProperty("classId").GetGuid() == foreign);
    }

    [Fact]
    public async Task The_overview_refuses_a_month_that_is_not_allowed()
    {
        var teacher = await factory.NewTeacherAsync();
        Assert.Equal("month_invalid", await Code(await teacher.Client.GetAsync("/api/fees/overview?month=2026-13")));
        Assert.Equal("month_in_future", await Code(await teacher.Client.GetAsync($"/api/fees/overview?month={Today.AddMonths(2):yyyy-MM}")));
    }

    [Fact]
    public async Task Attended_but_unpaid_lists_only_students_who_came_and_still_owe()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client, "Maths", 2000m);
        var paidUp = await AddStudent(teacher.Client, classId, "Paid");
        var owes = await AddStudent(teacher.Client, classId, "Owes");
        var late = await AddStudent(teacher.Client, classId, "Late");
        var absent = await AddStudent(teacher.Client, classId, "Absent");
        var free = await AddStudent(teacher.Client, classId, "Free", "FreeCard");
        await AddStudent(teacher.Client, classId, "Unmarked");
        await Pay(teacher.Client, paidUp, 2000m);
        await Pay(teacher.Client, late, 500m);

        // Nothing is listed before attendance is saved: a class nobody marked does not count.
        var before = await Json(await teacher.Client.GetAsync("/api/fees/unpaid-attendees"));
        Assert.Empty(before.GetProperty("rows").EnumerateArray());

        await TakeAttendance(teacher.Client, classId, (late, "Late"), (absent, "Absent"));

        var result = await Json(await teacher.Client.GetAsync($"/api/fees/unpaid-attendees?classId={classId}"));
        var rows = result.GetProperty("rows").EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("enrolmentId").GetGuid() == owes);
        var lateRow = rows.Single(r => r.GetProperty("enrolmentId").GetGuid() == late);
        Assert.Equal(1500m, lateRow.GetProperty("outstanding").GetDecimal());
        Assert.Equal(1, lateRow.GetProperty("attended").GetInt32());
        foreach (var excluded in new[] { paidUp, absent, free })
        {
            Assert.DoesNotContain(rows, r => r.GetProperty("enrolmentId").GetGuid() == excluded);
        }

        // Owes, Late and the unmarked-by-name student (marked present by default) all came and owe.
        Assert.Equal(3, rows.Count);
        Assert.Equal(2000m + 1500m + 2000m, result.GetProperty("outstanding").GetDecimal());

        // Paying clears the student from the list.
        await Pay(teacher.Client, owes, 2000m);
        var after = await Json(await teacher.Client.GetAsync($"/api/fees/unpaid-attendees?classId={classId}"));
        Assert.DoesNotContain(after.GetProperty("rows").EnumerateArray(), r => r.GetProperty("enrolmentId").GetGuid() == owes);
    }

    [Fact]
    public async Task Attended_but_unpaid_rejects_another_teachers_class()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync();
        var foreign = await DailyClass(other.Client, "Foreign", 1000m);

        var response = await teacher.Client.GetAsync($"/api/fees/unpaid-attendees?classId={foreign}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("class_not_found", await Code(response));
    }

    [Fact]
    public async Task The_fee_status_report_is_an_excel_file_with_every_student_and_a_total()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client, "Maths & Physics", 2000m);
        var aruni = await AddStudent(teacher.Client, classId, "Aruni");
        await AddStudent(teacher.Client, classId, "Bimal");
        await Pay(teacher.Client, aruni, 2000m);

        var response = await teacher.Client.GetAsync($"/api/reports/fee-status?classId={classId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxWriter.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".xlsx", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var sheet = SheetXml(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("Aruni Silva", sheet);
        Assert.Contains("Bimal Silva", sheet);
        Assert.Contains("Paid", sheet);
        Assert.Contains("Unpaid", sheet);
        Assert.Contains("Total", sheet);
        Assert.Contains("<v>4000</v>", sheet);
        Assert.Contains("<v>2000</v>", sheet);
    }

    [Fact]
    public async Task The_class_list_report_has_each_students_contact_details()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await DailyClass(teacher.Client, "Maths", 2000m);
        await AddStudent(teacher.Client, classId, "Aruni");
        await AddStudent(teacher.Client, classId, "Bimal", "FreeCard");

        var response = await teacher.Client.GetAsync($"/api/reports/class-list?classId={classId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sheet = SheetXml(await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("Aruni Silva", sheet);
        Assert.Contains("Free card", sheet);
        Assert.Contains("Guardian phone", sheet);
    }

    [Fact]
    public async Task Reports_refuse_unknown_reports_other_teachers_classes_and_strangers()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync();
        var foreign = await DailyClass(other.Client, "Foreign", 1000m);

        var unknown = await teacher.Client.GetAsync("/api/reports/nothing");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("report_not_found", await Code(unknown));

        var notYours = await teacher.Client.GetAsync($"/api/reports/class-list?classId={foreign}");
        Assert.Equal(HttpStatusCode.NotFound, notYours.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.Client.GetAsync($"/api/reports/fee-status?classId={foreign}")).StatusCode);
        Assert.Equal("class_not_found", await Code(await teacher.Client.GetAsync("/api/reports/class-list")));

        var stranger = await factory.CreateClient().GetAsync($"/api/reports/class-list?classId={foreign}");
        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
    }

    [Fact]
    public void The_excel_writer_escapes_text_keeps_numbers_numeric_and_cleans_the_sheet_name()
    {
        var table = new ReportTable(
            "Fees: A/L <Maths> [2027] with a very long title that goes on and on",
            ["Name", "Amount", "Date"],
            [["A & B <c>", 1500.5m, new DateOnly(2026, 10, 12)], ["=SUM(A1)", 7, null]]);

        var file = XlsxWriter.Write(table);
        var sheet = SheetXml(file);
        Assert.Contains("A &amp; B &lt;c&gt;", sheet);
        Assert.Contains("<v>1500.5</v>", sheet);
        Assert.Contains("<v>7</v>", sheet);
        Assert.Contains("<v>46307</v>", sheet);
        Assert.Contains("t=\"inlineStr\"", sheet);

        using var zip = new ZipArchive(new MemoryStream(file));
        using var workbook = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open());
        var name = XlsxWriter.SheetName(table.Title);
        Assert.True(name.Length <= 31);
        Assert.DoesNotContain(name, c => "[]:*?/\\".Contains(c));
        Assert.Contains($"name=\"{System.Security.SecurityElement.Escape(name)}\"", workbook.ReadToEnd());
        Assert.Equal("Report", XlsxWriter.SheetName("[]:"));
    }
}
