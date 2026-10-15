using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EduVibe.Api.Tests;

/// <summary>Plans and what they include, putting teachers on plans, hand-recorded payments, and the limits plans set.</summary>
public class PlansApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _counter = 100;
    private static int _phoneCounter = 6000;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private static string Day(int offset) => DateTime.UtcNow.AddHours(5.5).AddDays(offset).ToString("yyyy-MM-dd");

    /// <summary>Makes a plan with the given limits and features, with a name no other test uses.</summary>
    private async Task<Guid> NewPlan(object? limits = null, object? features = null, decimal price = 0, bool? isActive = null)
    {
        var admin = await factory.AdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/plans/plans", new
        {
            name = $"Plan {Interlocked.Increment(ref _counter)}", description = "Test plan", monthlyPrice = price, isActive,
            features = features ?? new { }, limits = limits ?? new { },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> Assign(Guid teacherId, Guid planId, string? startsOn = null, string? endsOn = null) =>
        await (await factory.AdminClientAsync()).PostAsJsonAsync($"/api/plans/teachers/{teacherId}/subscription", new { planId, startsOn, endsOn, note = "test" });

    private static async Task<JsonElement> Mine(HttpClient teacher) => await Json(await teacher.GetAsync("/api/plans/mine"));

    private async Task<Guid> NewClass(HttpClient teacher, string title, int day = 1)
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home " + title, district = "Kandy" });
        var venueId = (await Json(venue)).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2500m, slots = new[] { new { day, start = "16:00", end = "18:00" } },
        });
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddStudent(HttpClient teacher, Guid classId) =>
        teacher.PostAsJsonAsync("/api/students", new
        {
            phone = $"077{Interlocked.Increment(ref _phoneCounter):D7}", firstName = "Kamal", lastName = "Perera", classId, feePlan = "Full",
        });

    [Fact]
    public async Task Free_and_Pro_exist_from_the_start_and_Free_has_none_of_the_paid_features()
    {
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));

        var free = plans.EnumerateArray().Single(p => p.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Free", free.GetProperty("name").GetString());
        Assert.All(free.GetProperty("features").EnumerateObject(), f => Assert.False(f.Value.GetBoolean()));
        Assert.Equal(JsonValueKind.Null, free.GetProperty("limits").GetProperty("max_classes").ValueKind);
        // Free sends no WhatsApp messages at all, so it costs nothing in message fees.
        Assert.Equal(0, free.GetProperty("limits").GetProperty("whatsapp_per_month").GetInt32());

        var pro = plans.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Pro");
        Assert.True(pro.GetProperty("features").GetProperty("whatsapp").GetBoolean());
    }

    [Fact]
    public async Task A_new_teacher_is_on_Free_and_sees_locked_features_with_the_plan_that_has_them()
    {
        var teacher = await factory.NewTeacherAsync();

        var mine = await Mine(teacher.Client);

        Assert.Equal("Free", mine.GetProperty("planName").GetString());
        var whatsapp = mine.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "whatsapp");
        Assert.False(whatsapp.GetProperty("enabled").GetBoolean());
        Assert.False(string.IsNullOrEmpty(whatsapp.GetProperty("availableOn").GetString()));
    }

    [Fact]
    public async Task Only_the_Super_Admin_manages_plans_and_only_a_teacher_reads_their_own()
    {
        var teacher = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/plans/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync("/api/plans/plans", new { name = "Mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync($"/api/plans/teachers/{teacher.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/payments", new { amount = 1, method = "cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/plans/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/plans/mine")).StatusCode);
    }

    [Fact]
    public async Task A_plan_is_checked_before_it_is_saved()
    {
        var admin = await factory.AdminClientAsync();
        var taken = $"Plan {Interlocked.Increment(ref _counter)}";
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/plans/plans", new { name = taken })).StatusCode);

        async Task<string?> Try(object body) => await Code(await admin.PostAsJsonAsync("/api/plans/plans", body));

        Assert.Equal("name_required", await Try(new { name = "  " }));
        Assert.Equal("name_taken", await Try(new { name = taken.ToUpperInvariant() }));
        Assert.Equal("price_invalid", await Try(new { name = "A", monthlyPrice = -1 }));
        Assert.Equal("feature_invalid", await Try(new { name = "B", features = new { telepathy = true } }));
        Assert.Equal("limit_invalid", await Try(new { name = "C", limits = new { max_classes = -2 } }));
        Assert.Equal("limit_invalid", await Try(new { name = "D", limits = new { max_planets = 2 } }));
    }

    [Fact]
    public async Task Editing_a_plan_changes_what_a_teacher_on_it_can_use()
    {
        var teacher = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        var plan = await NewPlan(features: new { whatsapp = true }, limits: new { max_classes = 2 });
        await Assign(teacher.Id, plan);

        var before = await Mine(teacher.Client);
        Assert.True(before.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "whatsapp").GetProperty("enabled").GetBoolean());
        Assert.Equal(2, before.GetProperty("limits").EnumerateArray().Single(l => l.GetProperty("key").GetString() == "max_classes").GetProperty("limit").GetInt32());

        var updated = await admin.PutAsJsonAsync($"/api/plans/plans/{plan}", new
        {
            name = $"Plan {Interlocked.Increment(ref _counter)}", monthlyPrice = 1500, features = new { drive_materials = true }, limits = new { max_classes = 5 },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(1500m, (await Json(updated)).GetProperty("monthlyPrice").GetDecimal());

        var after = await Mine(teacher.Client);
        Assert.False(after.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "whatsapp").GetProperty("enabled").GetBoolean());
        Assert.True(after.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "drive_materials").GetProperty("enabled").GetBoolean());
        Assert.Equal(5, after.GetProperty("limits").EnumerateArray().Single(l => l.GetProperty("key").GetString() == "max_classes").GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task The_default_plan_cannot_be_switched_off_and_a_switched_off_plan_cannot_be_assigned()
    {
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var free = plans.EnumerateArray().Single(p => p.GetProperty("isDefault").GetBoolean());

        var off = await admin.PutAsJsonAsync($"/api/plans/plans/{free.GetProperty("id").GetGuid()}", new { name = "Free", isActive = false });
        Assert.Equal("default_plan_active", await Code(off));

        var retired = await NewPlan(isActive: false);
        var teacher = await factory.NewTeacherAsync();
        Assert.Equal("plan_invalid", await Code(await Assign(teacher.Id, retired)));
    }

    [Fact]
    public async Task Putting_a_teacher_on_a_plan_replaces_the_one_before_from_its_start_date()
    {
        var teacher = await factory.NewTeacherAsync();
        var gold = await NewPlan(features: new { parent_portal = true });
        var silver = await NewPlan(features: new { drive_materials = true });
        var admin = await factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await Assign(teacher.Id, gold, Day(-30))).StatusCode);
        var replaced = await Assign(teacher.Id, silver, Day(-5));
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);

        var body = await Json(replaced);
        Assert.Equal("Plan " + _counter, body.GetProperty("effective").GetProperty("planName").GetString());
        var subs = body.GetProperty("subscriptions").EnumerateArray().ToList();
        Assert.Equal(2, subs.Count);
        Assert.Equal(Day(-6), subs[1].GetProperty("endsOn").GetString());
        Assert.True(subs[0].GetProperty("inEffect").GetBoolean());

        // The teacher's own screen follows.
        var mine = await Mine(teacher.Client);
        Assert.True(mine.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "drive_materials").GetProperty("enabled").GetBoolean());
        Assert.False(mine.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "parent_portal").GetProperty("enabled").GetBoolean());

        var assignments = await Json(await admin.GetAsync("/api/plans/assignments"));
        Assert.Contains(assignments.EnumerateArray(), a => a.GetProperty("teacherId").GetGuid() == teacher.Id);
    }

    [Fact]
    public async Task A_plan_that_starts_later_waits_and_one_that_has_ended_gives_way_to_Free()
    {
        var future = await factory.NewTeacherAsync();
        var past = await factory.NewTeacherAsync();
        var pro = await NewPlan(features: new { whatsapp = true });

        await Assign(future.Id, pro, Day(10));
        await Assign(past.Id, pro, Day(-40), Day(-10));

        Assert.Equal("Free", (await Mine(future.Client)).GetProperty("planName").GetString());
        Assert.Equal("Free", (await Mine(past.Client)).GetProperty("planName").GetString());

        // Starting on today's date cancels the plan that was only waiting to start.
        var later = await NewPlan();
        var body = await Json(await Assign(future.Id, later, Day(0)));
        var statuses = body.GetProperty("subscriptions").EnumerateArray().Select(s => s.GetProperty("status").GetString()).ToList();
        Assert.Contains("cancelled", statuses);
    }

    [Fact]
    public async Task A_plan_assignment_is_checked()
    {
        var teacher = await factory.NewTeacherAsync();
        var plan = await NewPlan();
        var admin = await factory.AdminClientAsync();

        Assert.Equal("dates_invalid", await Code(await Assign(teacher.Id, plan, Day(5), Day(1))));
        Assert.Equal("plan_invalid", await Code(await Assign(teacher.Id, Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.NotFound, (await Assign(Guid.NewGuid(), plan)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/plans/teachers/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task A_payment_is_recorded_by_hand_against_the_current_plan()
    {
        var teacher = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        Task<HttpResponseMessage> Pay(object body) => admin.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/payments", body);

        // A teacher on the implicit Free has no subscription to pay for.
        Assert.Equal("subscription_required", await Code(await Pay(new { amount = 1500, method = "cash" })));

        var plan = await NewPlan(price: 1500);
        await Assign(teacher.Id, plan, Day(-3));

        Assert.Equal("amount_invalid", await Code(await Pay(new { amount = 0, method = "cash" })));
        Assert.Equal("method_invalid", await Code(await Pay(new { amount = 1500, method = "bitcoin" })));
        Assert.Equal("paid_on_in_future", await Code(await Pay(new { amount = 1500, method = "cash", paidOn = Day(2) })));

        var recorded = await Pay(new { amount = 1500.50m, method = "bank_transfer", paidOn = Day(-1), reference = "Ref 77" });
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var payment = (await Json(recorded)).GetProperty("payments").EnumerateArray().Single();
        Assert.Equal(1500.50m, payment.GetProperty("amount").GetDecimal());
        Assert.Equal("bank_transfer", payment.GetProperty("method").GetString());
        Assert.Equal("Ref 77", payment.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task A_plan_can_cap_the_active_classes_and_archiving_makes_room()
    {
        var teacher = await factory.NewTeacherAsync();
        await Assign(teacher.Id, await NewPlan(limits: new { max_classes = 1 }));

        var first = await NewClass(teacher.Client, "First");
        var venue = (await Json(await teacher.Client.PostAsJsonAsync("/api/classes/venues", new { name = "Hall", district = "Kandy" }))).GetProperty("id").GetGuid();
        Task<HttpResponseMessage> Second() => teacher.Client.PostAsJsonAsync("/api/classes/classes", new
        {
            title = "Second", subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId = venue, monthlyFee = 100m, slots = new[] { new { day = 2, start = "16:00", end = "18:00" } },
        });

        var blocked = await Second();
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("plan_limit_classes", await Code(blocked));

        await teacher.Client.PostAsync($"/api/classes/classes/{first}/archive", null);
        Assert.Equal(HttpStatusCode.Created, (await Second()).StatusCode);

        // Bringing the archived class back would be a second active one.
        var restore = await teacher.Client.PostAsync($"/api/classes/classes/{first}/restore", null);
        Assert.Equal("plan_limit_classes", await Code(restore));
    }

    [Fact]
    public async Task A_plan_can_cap_the_students_in_a_class_and_a_student_who_left_cannot_slip_back_in()
    {
        var teacher = await factory.NewTeacherAsync();
        await Assign(teacher.Id, await NewPlan(limits: new { max_students_per_class = 2 }));
        var classId = await NewClass(teacher.Client, "Crowded");

        var one = await AddStudent(teacher.Client, classId);
        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddStudent(teacher.Client, classId)).StatusCode);

        var third = await AddStudent(teacher.Client, classId);
        Assert.Equal(HttpStatusCode.Forbidden, third.StatusCode);
        Assert.Equal("plan_limit_students", await Code(third));

        // One leaves, a new one takes the place, and the one who left cannot come back until there is room again.
        var enrolmentId = (await Json(one)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
        var left = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{enrolmentId}", new { status = "Left" });
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddStudent(teacher.Client, classId)).StatusCode);
        var back = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{enrolmentId}", new { status = "Active" });
        Assert.Equal("plan_limit_students", await Code(back));
    }

    [Fact]
    public async Task A_refused_add_leaves_no_sign_in_behind_and_a_pending_student_cannot_skip_the_limit_by_an_update()
    {
        var teacher = await factory.NewTeacherAsync();
        await Assign(teacher.Id, await NewPlan(limits: new { max_students_per_class = 1 }));
        var classId = await NewClass(teacher.Client, "Tiny");
        var first = await Json(await AddStudent(teacher.Client, classId));
        var phone = $"077{Interlocked.Increment(ref _phoneCounter):D7}";
        object Body() => new { phone, firstName = "Late", lastName = "Comer", classId, feePlan = "Full" };

        var refused = await teacher.Client.PostAsJsonAsync("/api/students", Body());
        Assert.Equal("plan_limit_students", await Code(refused));

        // Someone signs up with the class link; the class is full, so approving by an update is refused like approving by Approve.
        var invite = await Json(await teacher.Client.PostAsJsonAsync("/api/students/invites", new { classId }));
        var token = invite.GetProperty("token").GetString()!;
        var registered = await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}",
            new { phone = $"077{Interlocked.Increment(ref _phoneCounter):D7}", firstName = "Dilani", lastName = "Silva", school = "S", grade = "G", password = "Student-pass-1" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var pending = (await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&status=pending")).EnumerateArray().Single();
        var sneak = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{pending.GetProperty("enrolmentId").GetGuid()}", new { status = "Active" });
        Assert.Equal("plan_limit_students", await Code(sneak));

        // Room appears; the add that was refused now works and the teacher gets the one-time password, so no account was left from the refusal.
        await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{first.GetProperty("row").GetProperty("enrolmentId").GetGuid()}", new { status = "Left" });
        var again = await teacher.Client.PostAsJsonAsync("/api/students", Body());
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(JsonValueKind.String, (await Json(again)).GetProperty("oneTimePassword").ValueKind);
    }

    [Fact]
    public async Task Plan_changes_are_in_the_audit_log_under_the_teacher()
    {
        var teacher = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        await Assign(teacher.Id, await NewPlan());
        await admin.PostAsJsonAsync($"/api/plans/teachers/{teacher.Id}/payments", new { amount = 500, method = "cash" });

        var log = await Json(await admin.GetAsync($"/api/reports/audit?teacherId={teacher.Id}&entity=TeacherSubscription"));

        var row = Assert.Single(log.GetProperty("items").EnumerateArray());
        Assert.Equal("created", row.GetProperty("action").GetString());
        Assert.Equal(teacher.Id, row.GetProperty("tenantId").GetGuid());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("userName").GetString()));

        var payments = await Json(await admin.GetAsync($"/api/reports/audit?teacherId={teacher.Id}&entity=PlanPayment"));
        Assert.Single(payments.GetProperty("items").EnumerateArray());
    }
}
