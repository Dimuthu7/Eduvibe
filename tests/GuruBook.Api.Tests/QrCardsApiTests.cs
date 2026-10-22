using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GuruBook.Api.Tests;

/// <summary>Student cards: making them, replacing a lost one, and looking one up when it is scanned for attendance.</summary>
public class QrCardsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 6000;

    private static string NewPhone() => $"076{Interlocked.Increment(ref _phones):D7}";

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private async Task<Guid> ClassOf(HttpClient teacher, string title)
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

    private static async Task<(Guid StudentId, Guid EnrolmentId)> Enrol(HttpClient teacher, Guid classId, string first)
    {
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone = NewPhone(), firstName = first, lastName = "Silva", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = (await Json(response)).GetProperty("row");
        return (row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid());
    }

    private static async Task<Guid> TodaysSession(HttpClient teacher, Guid classId)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));
        var sessions = await Json(await teacher.GetAsync($"/api/attendance/sessions?date={date:yyyy-MM-dd}"));
        return sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Scan(HttpClient teacher, Guid sessionId, string? code) =>
        teacher.PostAsJsonAsync($"/api/attendance/sessions/{sessionId}/scan", new { code });

    [Fact]
    public async Task A_class_gets_one_card_per_active_student_and_keeps_them()
    {
        var teacher = await factory.NewTeacherAsync("Card Maker");
        var classId = await ClassOf(teacher.Client, "Cards 1");
        await Enrol(teacher.Client, classId, "Amara");
        await Enrol(teacher.Client, classId, "Binara");

        var first = await Json(await teacher.Client.GetAsync($"/api/students/cards?classId={classId}"));
        Assert.Equal(2, first.GetArrayLength());
        var codes = first.EnumerateArray().Select(c => c.GetProperty("code").GetString()!).ToList();
        Assert.Equal(2, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Equal(22, code.Length));
        Assert.Equal([1, 2], first.EnumerateArray().Select(c => c.GetProperty("indexNumber").GetInt32()));

        var again = await Json(await teacher.Client.GetAsync($"/api/students/cards?classId={classId}"));
        Assert.Equal(codes, again.EnumerateArray().Select(c => c.GetProperty("code").GetString()!));

        Assert.Equal("class_required", await Code(await teacher.Client.GetAsync("/api/students/cards")));
    }

    [Fact]
    public async Task A_new_student_gets_a_card_when_the_sheet_is_printed_again()
    {
        var teacher = await factory.NewTeacherAsync("Late Joiner");
        var classId = await ClassOf(teacher.Client, "Cards 2");
        await Enrol(teacher.Client, classId, "Amara");
        Assert.Single((await Json(await teacher.Client.GetAsync($"/api/students/cards?classId={classId}"))).EnumerateArray());

        await Enrol(teacher.Client, classId, "Binara");
        Assert.Equal(2, (await Json(await teacher.Client.GetAsync($"/api/students/cards?classId={classId}"))).GetArrayLength());
    }

    [Fact]
    public async Task Scanning_a_card_finds_the_student_on_the_session()
    {
        var teacher = await factory.NewTeacherAsync("Scanner");
        var classId = await ClassOf(teacher.Client, "Cards 3");
        var (studentId, enrolmentId) = await Enrol(teacher.Client, classId, "Amara");
        var card = await Json(await teacher.Client.GetAsync($"/api/students/{studentId}/card?classId={classId}"));
        Assert.Equal(1, card.GetProperty("indexNumber").GetInt32());
        var session = await TodaysSession(teacher.Client, classId);

        var scanned = await Scan(teacher.Client, session, "EV1:" + card.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, scanned.StatusCode);
        var body = await Json(scanned);
        Assert.Equal(enrolmentId, body.GetProperty("enrolmentId").GetGuid());
        Assert.Equal("Amara Silva", body.GetProperty("fullName").GetString());

        // Looking a card up changes nothing: the student is still present, and no marks were saved.
        var detail = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions/{session}"));
        Assert.False(detail.GetProperty("session").GetProperty("taken").GetBoolean());
    }

    [Fact]
    public async Task Text_that_is_not_one_of_the_teachers_cards_is_refused()
    {
        var teacher = await factory.NewTeacherAsync("Careful");
        var classId = await ClassOf(teacher.Client, "Cards 4");
        var (studentId, _) = await Enrol(teacher.Client, classId, "Amara");
        var code = (await Json(await teacher.Client.GetAsync($"/api/students/{studentId}/card"))).GetProperty("code").GetString();
        var session = await TodaysSession(teacher.Client, classId);

        Assert.Equal("card_unknown", await Code(await Scan(teacher.Client, session, code)));
        Assert.Equal("card_unknown", await Code(await Scan(teacher.Client, session, "EV1:" + new string('A', 22))));
        Assert.Equal("card_unknown", await Code(await Scan(teacher.Client, session, "https://example.com/" + code)));
        Assert.Equal("card_unknown", await Code(await Scan(teacher.Client, session, "EV1:" + new string('A', 200))));
        Assert.Equal("card_unknown", await Code(await Scan(teacher.Client, session, null)));
        Assert.Equal(HttpStatusCode.NotFound, (await Scan(teacher.Client, Guid.NewGuid(), "EV1:" + code)).StatusCode);
    }

    [Fact]
    public async Task A_card_from_another_class_is_named_as_not_in_this_one()
    {
        var teacher = await factory.NewTeacherAsync("Two Classes");
        var maths = await ClassOf(teacher.Client, "Cards 5a");
        var science = await ClassOf(teacher.Client, "Cards 5b");
        var (inScience, _) = await Enrol(teacher.Client, science, "Binara");
        await Enrol(teacher.Client, maths, "Amara");
        var code = (await Json(await teacher.Client.GetAsync($"/api/students/{inScience}/card"))).GetProperty("code").GetString();

        var result = await Scan(teacher.Client, await TodaysSession(teacher.Client, maths), "EV1:" + code);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("card_not_in_class", await Code(result));
    }

    [Fact]
    public async Task Another_teachers_cards_and_students_stay_out_of_reach()
    {
        var owner = await factory.NewTeacherAsync("Owner");
        var other = await factory.NewTeacherAsync("Other");
        var ownerClass = await ClassOf(owner.Client, "Cards 6a");
        var otherClass = await ClassOf(other.Client, "Cards 6b");
        var (studentId, _) = await Enrol(owner.Client, ownerClass, "Amara");
        await Enrol(other.Client, otherClass, "Binara");
        var code = (await Json(await owner.Client.GetAsync($"/api/students/{studentId}/card"))).GetProperty("code").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/students/{studentId}/card")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/students/{studentId}/card/replace", null)).StatusCode);
        Assert.Equal("card_unknown", await Code(await Scan(other.Client, await TodaysSession(other.Client, otherClass), "EV1:" + code)));
    }

    [Fact]
    public async Task Replacing_a_lost_card_stops_the_old_one_working()
    {
        var teacher = await factory.NewTeacherAsync("Replacer");
        var classId = await ClassOf(teacher.Client, "Cards 7");
        var (studentId, _) = await Enrol(teacher.Client, classId, "Amara");
        var oldCode = (await Json(await teacher.Client.GetAsync($"/api/students/{studentId}/card"))).GetProperty("code").GetString();
        var session = await TodaysSession(teacher.Client, classId);

        var replaced = await teacher.Client.PostAsync($"/api/students/{studentId}/card/replace", null);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var newCode = (await Json(replaced)).GetProperty("code").GetString();
        Assert.NotEqual(oldCode, newCode);

        Assert.Equal("card_unknown", await Code(await Scan(teacher.Client, session, "EV1:" + oldCode)));
        Assert.Equal(HttpStatusCode.OK, (await Scan(teacher.Client, session, "EV1:" + newCode)).StatusCode);
    }

    [Fact]
    public async Task Students_and_parents_cannot_use_the_card_endpoints()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/students/cards?classId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/attendance/sessions/{Guid.NewGuid()}/scan", new { code = "EV1:x" })).StatusCode);
    }
}
