using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EduVibe.Api.Tests;

public class ClassesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<Guid> InstituteFor(params Guid[] teacherIds)
    {
        var admin = await factory.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/classes/institutes", new { name = $"Institute {Guid.NewGuid():N}"[..20], district = "Colombo" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PutAsJsonAsync($"/api/classes/institutes/{id}/teachers", new { teacherIds });
        return id;
    }

    private static async Task<Guid> VenueFor(HttpClient teacher, string name = "Home class")
    {
        var response = await teacher.PostAsJsonAsync("/api/classes/venues", new { name, district = "Kandy" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private object ClassBody(Guid? instituteId, Guid? venueId, object[]? slots = null, string title = "2027 A/L Maths", decimal fee = 2500m, int year = 2027) =>
        new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = year, medium = "English",
            instituteId, venueId, monthlyFee = fee,
            slots = slots ?? [new { day = 1, start = "16:00", end = "18:00" }],
        };

    private static async Task<(HttpStatusCode Status, string? Code)> Code(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return (response.StatusCode, null);
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Teacher_adds_a_private_venue_that_only_they_see()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Kamala");
        await VenueFor(teacher.Client, "My hall");

        var mine = await teacher.Client.GetFromJsonAsync<JsonElement>("/api/classes/venues");
        Assert.Equal(["My hall"], mine.EnumerateArray().Select(v => v.GetProperty("name").GetString()));
        var theirs = await other.Client.GetFromJsonAsync<JsonElement>("/api/classes/venues");
        Assert.Empty(theirs.EnumerateArray());

        var noDistrict = await teacher.Client.PostAsJsonAsync("/api/classes/venues", new { name = "Nowhere" });
        Assert.Equal((HttpStatusCode.BadRequest, "district_invalid"), await Code(noDistrict));
    }

    [Fact]
    public async Task Teacher_creates_a_class_at_an_institute_and_a_home_venue_and_sees_both()
    {
        var teacher = await factory.NewTeacherAsync();
        var institute = await InstituteFor(teacher.Id);
        var venue = await VenueFor(teacher.Client);

        var atInstitute = await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(institute, null, title: "Maths at institute"));
        var atHome = await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(null, venue, [new { day = 3, start = "09:00", end = "11:00" }], "Maths at home"));
        Assert.Equal(HttpStatusCode.Created, atInstitute.StatusCode);
        Assert.Equal(HttpStatusCode.Created, atHome.StatusCode);

        var created = await atHome.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("venue", created.GetProperty("placeType").GetString());
        Assert.Equal("Home class", created.GetProperty("placeName").GetString());
        Assert.Equal("Active", created.GetProperty("status").GetString());
        Assert.Equal(2500m, created.GetProperty("monthlyFee").GetDecimal());

        var list = await teacher.Client.GetFromJsonAsync<JsonElement>("/api/classes/classes");
        Assert.Equal(["Maths at home", "Maths at institute"], list.EnumerateArray().Select(c => c.GetProperty("title").GetString()));

        var filtered = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/classes/classes?instituteId={institute}");
        Assert.Equal(["Maths at institute"], filtered.EnumerateArray().Select(c => c.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task A_class_needs_valid_details_and_a_place_the_teacher_can_use()
    {
        var teacher = await factory.NewTeacherAsync();
        var stranger = await factory.NewTeacherAsync("Stranger");
        var linked = await InstituteFor(teacher.Id);
        var notLinked = await InstituteFor(stranger.Id);
        var strangersVenue = await VenueFor(stranger.Client);
        var venue = await VenueFor(teacher.Client);

        async Task<(HttpStatusCode, string?)> Try(object body) => await Code(await teacher.Client.PostAsJsonAsync("/api/classes/classes", body));

        Assert.Equal((HttpStatusCode.BadRequest, "title_required"), await Try(ClassBody(linked, null, title: " ")));
        Assert.Equal((HttpStatusCode.BadRequest, "exam_year_invalid"), await Try(ClassBody(linked, null, year: 1990)));
        Assert.Equal((HttpStatusCode.BadRequest, "fee_invalid"), await Try(ClassBody(linked, null, fee: -1)));
        Assert.Equal((HttpStatusCode.BadRequest, "place_required"), await Try(ClassBody(null, null)));
        Assert.Equal((HttpStatusCode.BadRequest, "place_required"), await Try(ClassBody(linked, venue)));
        Assert.Equal((HttpStatusCode.BadRequest, "place_invalid"), await Try(ClassBody(notLinked, null)));
        Assert.Equal((HttpStatusCode.BadRequest, "place_invalid"), await Try(ClassBody(null, strangersVenue)));
        Assert.Equal((HttpStatusCode.BadRequest, "slots_required"), await Try(ClassBody(linked, null, slots: [])));
        Assert.Equal((HttpStatusCode.BadRequest, "slot_invalid"), await Try(ClassBody(linked, null, [new { day = 1, start = "18:00", end = "16:00" }])));
        Assert.Equal((HttpStatusCode.BadRequest, "slot_invalid"), await Try(ClassBody(linked, null, [new { day = 8, start = "16:00", end = "18:00" }])));
        Assert.Equal((HttpStatusCode.BadRequest, "slots_overlap"),
            await Try(ClassBody(linked, null, [new { day = 1, start = "16:00", end = "18:00" }, new { day = 1, start = "17:00", end = "19:00" }])));

        var badMedium = new { title = "x", subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "French", instituteId = linked, monthlyFee = 1m, slots = new[] { new { day = 1, start = "16:00", end = "18:00" } } };
        Assert.Equal((HttpStatusCode.BadRequest, "medium_invalid"), await Try(badMedium));
        var badSubject = new { title = "x", subjectId = Guid.NewGuid(), streamId = factory.StreamId, examYear = 2027, medium = "English", instituteId = linked, monthlyFee = 1m, slots = new[] { new { day = 1, start = "16:00", end = "18:00" } } };
        Assert.Equal((HttpStatusCode.BadRequest, "subject_invalid"), await Try(badSubject));
    }

    [Fact]
    public async Task Teacher_edits_a_class_and_replaces_its_weekly_slots()
    {
        var teacher = await factory.NewTeacherAsync();
        var venue = await VenueFor(teacher.Client);
        var created = await (await teacher.Client.PostAsJsonAsync("/api/classes/classes",
            ClassBody(null, venue, [new { day = 1, start = "16:00", end = "18:00" }, new { day = 4, start = "16:00", end = "18:00" }]))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(2, created.GetProperty("slots").GetArrayLength());

        var updated = await teacher.Client.PutAsJsonAsync($"/api/classes/classes/{id}",
            ClassBody(null, venue, [new { day = 5, start = "08:30", end = "10:30" }], "Renamed", 3000m));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed", body.GetProperty("title").GetString());
        var slot = Assert.Single(body.GetProperty("slots").EnumerateArray());
        Assert.Equal(5, slot.GetProperty("day").GetInt32());
        Assert.Equal("08:30:00", slot.GetProperty("start").GetString());

        var reread = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/classes/classes/{id}");
        Assert.Equal(1, reread.GetProperty("slots").GetArrayLength());
    }

    [Fact]
    public async Task Archived_classes_leave_the_active_list_stay_readable_and_can_be_restored()
    {
        var teacher = await factory.NewTeacherAsync();
        var venue = await VenueFor(teacher.Client);
        var id = (await (await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(null, venue))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var archived = await teacher.Client.PostAsync($"/api/classes/classes/{id}/archive", null);
        Assert.Equal("Archived", (await archived.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>("/api/classes/classes")).EnumerateArray());
        Assert.Single((await teacher.Client.GetFromJsonAsync<JsonElement>("/api/classes/classes?status=archived")).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.GetAsync($"/api/classes/classes/{id}")).StatusCode);
        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>("/api/classes/timetable")).EnumerateArray());

        var edit = await teacher.Client.PutAsJsonAsync($"/api/classes/classes/{id}", ClassBody(null, venue));
        Assert.Equal((HttpStatusCode.Conflict, "class_archived"), await Code(edit));

        await teacher.Client.PostAsync($"/api/classes/classes/{id}/restore", null);
        Assert.Single((await teacher.Client.GetFromJsonAsync<JsonElement>("/api/classes/classes")).EnumerateArray());
    }

    [Fact]
    public async Task Teachers_never_see_each_others_classes()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Kamala");
        var venue = await VenueFor(teacher.Client);
        var id = (await (await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(null, venue))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Empty((await other.Client.GetFromJsonAsync<JsonElement>("/api/classes/classes?status=all")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/classes/classes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/classes/classes/{id}/archive", null)).StatusCode);

        var admin = await factory.AdminClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/classes/classes")).StatusCode);
    }

    [Fact]
    public async Task Timetable_lists_slots_by_day_and_flags_overlaps_even_across_institutes()
    {
        var teacher = await factory.NewTeacherAsync();
        var institute = await InstituteFor(teacher.Id);
        var venue = await VenueFor(teacher.Client);
        await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(institute, null, [new { day = 2, start = "16:00", end = "18:00" }], "Maths A"));
        await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(null, venue, [new { day = 2, start = "17:30", end = "19:00" }], "Science B"));
        await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(null, venue, [new { day = 2, start = "19:00", end = "20:00" }], "Maths C"));

        var all = (await teacher.Client.GetFromJsonAsync<JsonElement>("/api/classes/timetable")).EnumerateArray().ToList();
        Assert.Equal(["Maths A", "Science B", "Maths C"], all.Select(e => e.GetProperty("title").GetString()));
        Assert.Single(all[0].GetProperty("overlapsWith").EnumerateArray());
        Assert.Single(all[1].GetProperty("overlapsWith").EnumerateArray());
        // Back to back is not an overlap.
        Assert.Empty(all[2].GetProperty("overlapsWith").EnumerateArray());

        var filtered = (await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/classes/timetable?instituteId={institute}")).EnumerateArray().ToList();
        Assert.Equal(["Maths A"], filtered.Select(e => e.GetProperty("title").GetString()));
        Assert.Single(filtered[0].GetProperty("overlapsWith").EnumerateArray());
    }
}
