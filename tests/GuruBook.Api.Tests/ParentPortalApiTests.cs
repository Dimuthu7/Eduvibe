using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GuruBook.Api.Tests;

/// <summary>The parent portal: linking a parent to a child, what a parent sees across teachers, plan gating, and how links end.</summary>
public class ParentPortalApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 2000;

    private static string NewPhone() => $"075{Interlocked.Increment(ref _phones):D7}";

    private static string International(string local) => "+94" + local[1..];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private async Task SetPlan(Guid teacherId, string name)
    {
        var admin = await factory.AdminClientAsync();
        var plans = await Json(await admin.GetAsync("/api/plans/plans"));
        var plan = plans.EnumerateArray().Single(p => p.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/plans/teachers/{teacherId}/subscription", new { planId = plan, note = "test" })).StatusCode);
    }

    private async Task<TestTeacher> ProTeacher(string name)
    {
        var teacher = await factory.NewTeacherAsync(name);
        await SetPlan(teacher.Id, "Pro");
        return teacher;
    }

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

    private sealed record Child(Guid StudentId, Guid EnrolmentId, string Phone);

    private static async Task<Child> Enrol(HttpClient teacher, Guid classId, string first, string? guardianPhone, string? guardianName = "Mrs Fernando")
    {
        var phone = NewPhone();
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Silva", classId, feePlan = "Full", guardianPhone, guardianName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = (await Json(response)).GetProperty("row");
        return new Child(row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), phone);
    }

    private HttpClient Bearer(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Signs in with the one-time password, chooses a username and password, and returns the signed-in client.</summary>
    private async Task<HttpClient> SignIn(string phone, string otp)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setup = Bearer((await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = otp, newPassword = "Parent-pass-1", username = "p" + Guid.NewGuid().ToString("N")[..10] });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        return Bearer((await Json(changed)).GetProperty("accessToken").GetString()!);
    }

    private static async Task<JsonElement> Invite(HttpClient teacher, Guid studentId)
    {
        var response = await teacher.PostAsync($"/api/students/{studentId}/parent", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    private static Task<JsonElement> Family(HttpClient parent) => Get(parent, "/api/students/family");

    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    [Fact]
    public async Task A_free_teacher_cannot_invite_a_parent_and_a_pro_teacher_needs_a_guardian_phone()
    {
        var free = await factory.NewTeacherAsync("Freda");
        var freeChild = await Enrol(free.Client, await ClassOf(free.Client, "Free class"), "Kamal", NewPhone());
        Assert.Equal("plan_feature_locked", await Code(await free.Client.PostAsync($"/api/students/{freeChild.StudentId}/parent", null)));

        var pro = await ProTeacher("Priya");
        var classId = await ClassOf(pro.Client, "Pro class");
        var noGuardian = await Enrol(pro.Client, classId, "Nimali", null);
        Assert.Equal("guardian_phone_missing", await Code(await pro.Client.PostAsync($"/api/students/{noGuardian.StudentId}/parent", null)));

        // The guardian's number must not be the student's own, and a teacher cannot reach a student of someone else's.
        var self = await Enrol(pro.Client, classId, "Self", International(NewPhone()));
        Assert.Equal(HttpStatusCode.NoContent, (await pro.Client.PutAsJsonAsync($"/api/students/{self.StudentId}", new { firstName = "Self", lastName = "Silva", guardianPhone = International(self.Phone) })).StatusCode);
        Assert.Equal("guardian_phone_is_student", await Code(await pro.Client.PostAsync($"/api/students/{self.StudentId}/parent", null)));
        Assert.Equal(HttpStatusCode.NotFound, (await pro.Client.PostAsync($"/api/students/{freeChild.StudentId}/parent", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync($"/api/students/{noGuardian.StudentId}/parent", null)).StatusCode);
    }

    [Fact]
    public async Task A_parent_signs_in_with_the_guardian_phone_and_sees_two_children_at_different_teachers()
    {
        var guardian = NewPhone();
        var maths = await ProTeacher("Maya");
        var science = await ProTeacher("Sena");
        var mathsClass = await ClassOf(maths.Client, "Parent Maths");
        var scienceClass = await ClassOf(science.Client, "Parent Science");
        var kamal = await Enrol(maths.Client, mathsClass, "Kamal", guardian);
        var nimali = await Enrol(science.Client, scienceClass, "Nimali", guardian);

        var first = await Invite(maths.Client, kamal.StudentId);
        Assert.True(first.GetProperty("accountCreated").GetBoolean());
        Assert.Equal(International(guardian), first.GetProperty("phone").GetString());
        Assert.Equal("Mrs Fernando", first.GetProperty("name").GetString());
        var otp = first.GetProperty("oneTimePassword").GetString()!;

        // The second teacher links the same number: the account exists, so no new password is given out.
        var second = await Invite(science.Client, nimali.StudentId);
        Assert.False(second.GetProperty("accountCreated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("oneTimePassword").ValueKind);
        // Inviting again changes nothing.
        Assert.False((await Invite(maths.Client, kamal.StudentId)).GetProperty("accountCreated").GetBoolean());

        var parent = await SignIn(International(guardian), otp);
        var children = (await Family(parent)).EnumerateArray().ToArray();
        Assert.Equal(["Kamal Silva", "Nimali Silva"], children.Select(c => c.GetProperty("fullName").GetString()));
        var kamalClasses = children[0].GetProperty("classes").EnumerateArray().Single();
        Assert.Equal("Parent Maths", kamalClasses.GetProperty("classTitle").GetString());
        Assert.Equal("Maya Teacher", kamalClasses.GetProperty("teacherName").GetString());
        Assert.False(kamalClasses.GetProperty("locked").GetBoolean());
        Assert.Equal(7, kamalClasses.GetProperty("slots").GetArrayLength());
        Assert.Equal("Sena Teacher", children[1].GetProperty("classes")[0].GetProperty("teacherName").GetString());

        // The teacher's student page says a parent is linked.
        Assert.True((await Get(maths.Client, $"/api/students/{kamal.StudentId}")).GetProperty("hasParent").GetBoolean());
    }

    [Fact]
    public async Task A_parent_sees_fees_attendance_and_only_published_results_of_their_own_child()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Tara");
        var classId = await ClassOf(teacher.Client, "Parent Data");
        var kamal = await Enrol(teacher.Client, classId, "Kamal", guardian);
        var other = await Enrol(teacher.Client, classId, "Other", NewPhone());
        var otp = (await Invite(teacher.Client, kamal.StudentId)).GetProperty("oneTimePassword").GetString()!;
        var parent = await SignIn(International(guardian), otp);

        Assert.True((await teacher.Client.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = kamal.EnrolmentId, amount = 500m, method = "Cash" })).IsSuccessStatusCode);
        var fees = (await Get(parent, $"/api/fees/children/{kamal.StudentId}")).EnumerateArray().Single();
        Assert.Equal(classId, fees.GetProperty("classId").GetGuid());
        Assert.Equal(1500m, fees.GetProperty("outstanding").GetDecimal());
        Assert.Equal(1, fees.GetProperty("receipts").GetArrayLength());

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);
        var sessions = await Json(await teacher.Client.GetAsync($"/api/attendance/sessions?date={today:yyyy-MM-dd}"));
        var sessionId = sessions.EnumerateArray().Single(s => s.GetProperty("classId").GetGuid() == classId).GetProperty("id").GetGuid();
        Assert.True((await teacher.Client.PutAsJsonAsync($"/api/attendance/sessions/{sessionId}/marks", new
        {
            marks = new[] { new { enrolmentId = kamal.EnrolmentId, status = "Present" }, new { enrolmentId = other.EnrolmentId, status = "Absent" } },
        })).IsSuccessStatusCode);
        var attendance = (await Get(parent, $"/api/attendance/children/{kamal.StudentId}")).EnumerateArray().Single();
        Assert.Equal(1, attendance.GetProperty("sessions").GetInt32());
        Assert.Equal(1, attendance.GetProperty("present").GetInt32());
        Assert.Equal(100, attendance.GetProperty("percent").GetInt32());

        var exam = await Json(await teacher.Client.PostAsJsonAsync("/api/exams", new
        {
            classId, name = "Term 1", heldOn = today, parts = new[] { new { name = "Paper", maxMark = 100m } },
        }));
        var examId = exam.GetProperty("id").GetGuid();
        var part = exam.GetProperty("parts")[0].GetProperty("id").GetGuid();
        Assert.True((await teacher.Client.PutAsJsonAsync($"/api/exams/{examId}/marks", new
        {
            rows = new[]
            {
                new { enrolmentId = kamal.EnrolmentId, absent = false, marks = new Dictionary<Guid, decimal?> { [part] = 80m } },
                new { enrolmentId = other.EnrolmentId, absent = false, marks = new Dictionary<Guid, decimal?> { [part] = 40m } },
            },
        })).IsSuccessStatusCode);
        Assert.Empty((await Get(parent, $"/api/exams/children/{kamal.StudentId}")).EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsync($"/api/exams/{examId}/publish", null)).StatusCode);
        var results = (await Get(parent, $"/api/exams/children/{kamal.StudentId}")).EnumerateArray().Single();
        Assert.Equal("Term 1", results.GetProperty("name").GetString());
        Assert.Equal(80m, results.GetProperty("total").GetDecimal());
        Assert.Equal(1, results.GetProperty("rank").GetInt32());

        // A child nobody linked to this parent returns nothing, not an error that confirms the child exists.
        Assert.Empty((await Get(parent, $"/api/fees/children/{other.StudentId}")).EnumerateArray());
        Assert.Empty((await Get(parent, $"/api/attendance/children/{other.StudentId}")).EnumerateArray());
        Assert.Empty((await Get(parent, $"/api/exams/children/{other.StudentId}")).EnumerateArray());
    }

    [Fact]
    public async Task Roles_do_not_cross_a_parent_cannot_use_teacher_or_student_screens_and_they_cannot_use_the_parent_ones()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Rina");
        var classId = await ClassOf(teacher.Client, "Roles class");
        var kamal = await Enrol(teacher.Client, classId, "Kamal", guardian);
        var parent = await SignIn(International(guardian), (await Invite(teacher.Client, kamal.StudentId)).GetProperty("oneTimePassword").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/fees/mine")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.PostAsync($"/api/students/{kamal.StudentId}/parent", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/classes/classes")).StatusCode);

        var student = await SignIn(International(kamal.Phone), (await Json(await teacher.Client.PostAsync($"/api/students/{kamal.StudentId}/reset-password", null))).GetProperty("oneTimePassword").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/students/family")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/students/family")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync($"/api/fees/children/{kamal.StudentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/students/family")).StatusCode);
    }

    [Fact]
    public async Task A_teacher_who_drops_the_parent_portal_locks_their_classes_for_the_parent()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Dina");
        var classId = await ClassOf(teacher.Client, "Lapsing class");
        var kamal = await Enrol(teacher.Client, classId, "Kamal", guardian);
        var parent = await SignIn(International(guardian), (await Invite(teacher.Client, kamal.StudentId)).GetProperty("oneTimePassword").GetString()!);
        Assert.Single((await Get(parent, $"/api/fees/children/{kamal.StudentId}")).EnumerateArray());

        await SetPlan(teacher.Id, "Free");
        var cls = (await Family(parent)).EnumerateArray().Single().GetProperty("classes")[0];
        Assert.True(cls.GetProperty("locked").GetBoolean());
        Assert.Empty((await Get(parent, $"/api/fees/children/{kamal.StudentId}")).EnumerateArray());
        Assert.Empty((await Get(parent, $"/api/attendance/children/{kamal.StudentId}")).EnumerateArray());
        Assert.Empty((await Get(parent, $"/api/exams/children/{kamal.StudentId}")).EnumerateArray());

        // The link is kept: going back to Pro turns the screens on again.
        await SetPlan(teacher.Id, "Pro");
        Assert.Single((await Get(parent, $"/api/fees/children/{kamal.StudentId}")).EnumerateArray());
    }

    [Fact]
    public async Task A_child_who_has_left_the_class_disappears_from_the_parent()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Lena");
        var classId = await ClassOf(teacher.Client, "Leaving class");
        var kamal = await Enrol(teacher.Client, classId, "Kamal", guardian);
        var parent = await SignIn(International(guardian), (await Invite(teacher.Client, kamal.StudentId)).GetProperty("oneTimePassword").GetString()!);
        Assert.Single((await Family(parent)).EnumerateArray());

        Assert.True((await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{kamal.EnrolmentId}", new { status = "Left" })).IsSuccessStatusCode);
        Assert.Empty((await Family(parent)).EnumerateArray());
        Assert.Empty((await Get(parent, $"/api/fees/children/{kamal.StudentId}")).EnumerateArray());
    }

    [Fact]
    public async Task Removing_the_link_resetting_the_password_and_changing_the_guardian_end_or_renew_access()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Mala");
        var classId = await ClassOf(teacher.Client, "Links class");
        var kamal = await Enrol(teacher.Client, classId, "Kamal", guardian);
        var nimali = await Enrol(teacher.Client, classId, "Nimali", guardian);
        var otp = (await Invite(teacher.Client, kamal.StudentId)).GetProperty("oneTimePassword").GetString()!;
        await Invite(teacher.Client, nimali.StudentId);

        // A reset gives a new one-time password; the old one no longer works.
        var newOtp = (await Json(await teacher.Client.PostAsync($"/api/students/{kamal.StudentId}/parent/reset-password", null))).GetProperty("oneTimePassword").GetString()!;
        Assert.NotEqual(otp, newOtp);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = International(guardian), password = otp })).StatusCode);
        var parent = await SignIn(International(guardian), newOtp);
        Assert.Equal(2, (await Family(parent)).GetArrayLength());

        // Removing one child's link leaves the other.
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/students/{nimali.StudentId}/parent")).StatusCode);
        Assert.False((await Get(teacher.Client, $"/api/students/{nimali.StudentId}")).GetProperty("hasParent").GetBoolean());

        // A new guardian number drops every link for the child, and the parent who has no child left has no account either.
        var changed = await teacher.Client.PutAsJsonAsync($"/api/students/{kamal.StudentId}", new { firstName = "Kamal", lastName = "Silva", guardianPhone = International(NewPhone()) });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.False((await Get(teacher.Client, $"/api/students/{kamal.StudentId}")).GetProperty("hasParent").GetBoolean());
        var third = await Enrol(teacher.Client, classId, "Third", guardian);
        Assert.True((await Invite(teacher.Client, third.StudentId)).GetProperty("accountCreated").GetBoolean());
    }

    [Fact]
    public async Task A_teacher_who_is_also_a_guardian_keeps_their_teacher_account_and_gains_the_parent_role()
    {
        var other = await factory.NewTeacherAsync("Both");
        var teacher = await ProTeacher("Host");
        var classId = await ClassOf(teacher.Client, "Both class");
        var me = await Json(await other.Client.GetAsync("/api/identity/me"));
        var phone = me.GetProperty("phone").GetString()!;
        var kamal = await Enrol(teacher.Client, classId, "Kamal", "0" + phone[3..]);
        var invited = await Invite(teacher.Client, kamal.StudentId);
        Assert.False(invited.GetProperty("accountCreated").GetBoolean());

        // Their own sign-in is untouched: no password is handed out, and another teacher cannot reset a staff account's password.
        Assert.Equal(JsonValueKind.Null, invited.GetProperty("oneTimePassword").ValueKind);
        Assert.Equal("account_not_resettable", await Code(await teacher.Client.PostAsync($"/api/students/{kamal.StudentId}/parent/reset-password", null)));
    }

    [Fact]
    public async Task Erasing_a_teacher_ends_their_links_and_a_parent_with_no_other_child_loses_the_account()
    {
        var shared = NewPhone();
        var onlyHere = NewPhone();
        var leaving = await ProTeacher("Gone");
        var staying = await ProTeacher("Stay");
        var kamal = await Enrol(leaving.Client, await ClassOf(leaving.Client, "Gone class"), "Kamal", shared);
        var nimali = await Enrol(staying.Client, await ClassOf(staying.Client, "Stay class"), "Nimali", shared);
        var loner = await Enrol(leaving.Client, await ClassOf(leaving.Client, "Gone class 2"), "Loner", onlyHere);
        var otp = (await Invite(leaving.Client, kamal.StudentId)).GetProperty("oneTimePassword").GetString()!;
        await Invite(staying.Client, nimali.StudentId);
        await Invite(leaving.Client, loner.StudentId);
        var parent = await SignIn(International(shared), otp);
        Assert.Equal(2, (await Family(parent)).GetArrayLength());

        var admin = await factory.AdminClientAsync();
        Assert.True((await admin.PutAsJsonAsync($"/api/identity/teachers/{leaving.Id}/active", new { isActive = false })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/privacy/teachers/{leaving.Id}/erase", new { confirm = "Gone Teacher" })).StatusCode);

        // The parent keeps the child who is still with another teacher; the parent of a child nobody teaches any more is gone.
        Assert.Equal(["Nimali Silva"], (await Family(parent)).EnumerateArray().Select(c => c.GetProperty("fullName").GetString()));
        var again = await Enrol(staying.Client, await ClassOf(staying.Client, "Stay class 2"), "Again", shared);
        Assert.False((await Invite(staying.Client, again.StudentId)).GetProperty("accountCreated").GetBoolean());
        var reuse = await Enrol(staying.Client, await ClassOf(staying.Client, "Stay class 3"), "Reuse", onlyHere);
        Assert.True((await Invite(staying.Client, reuse.StudentId)).GetProperty("accountCreated").GetBoolean());
    }

    [Fact]
    public async Task Erasing_a_student_removes_the_parents_view_of_them()
    {
        var guardian = NewPhone();
        var teacher = await ProTeacher("Erin");
        var classId = await ClassOf(teacher.Client, "Erase class");
        var kamal = await Enrol(teacher.Client, classId, "Kamal", guardian);
        var nimali = await Enrol(teacher.Client, classId, "Nimali", guardian);
        var otp = (await Invite(teacher.Client, kamal.StudentId)).GetProperty("oneTimePassword").GetString()!;
        await Invite(teacher.Client, nimali.StudentId);
        var parent = await SignIn(International(guardian), otp);

        var admin = await factory.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/privacy/students/{kamal.StudentId}/erase", new { confirm = "Kamal Silva" })).StatusCode);
        Assert.Equal(["Nimali Silva"], (await Family(parent)).EnumerateArray().Select(c => c.GetProperty("fullName").GetString()));
        Assert.Empty((await Get(parent, $"/api/fees/children/{kamal.StudentId}")).EnumerateArray());

        // Erasing the last child takes the parent role, and the account, with it.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/privacy/students/{nimali.StudentId}/erase", new { confirm = "Nimali Silva" })).StatusCode);
        var third = await Enrol(teacher.Client, classId, "Third", guardian);
        Assert.True((await Invite(teacher.Client, third.StudentId)).GetProperty("accountCreated").GetBoolean());
    }
}
