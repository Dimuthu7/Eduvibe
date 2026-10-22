using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GuruBook.Api.Tests;

public class StudentsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 1000;

    private string NewPhone() => $"077{Interlocked.Increment(ref _phoneCounter):D7}";

    private async Task<Guid> ClassFor(HttpClient teacher, string title = "2027 A/L Maths", decimal fee = 2500m)
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home class", district = "Kandy" });
        var venueId = (await venue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = fee, slots = new[] { new { day = 1, start = "16:00", end = "18:00" } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object StudentBody(Guid classId, string phone, string first = "Kasun", object? extra = null) => new
    {
        phone, firstName = first, lastName = "Perera", school = "Royal College", grade = "Grade 11",
        guardianName = "Sunil Perera", guardianPhone = "0711234567", classId, feePlan = "Full",
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<(HttpStatusCode Status, string? Code)> Code(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return (response.StatusCode, null);
        return (response.StatusCode, (await Json(response)).GetProperty("code").GetString());
    }

    private async Task<HttpStatusCode> LoginStatus(string username, string password) =>
        (await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username, password })).StatusCode;

    [Fact]
    public async Task Teacher_adds_a_student_who_gets_a_one_time_password_and_the_next_index_number()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var phone = NewPhone();

        var first = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, phone));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        var otp = body.GetProperty("oneTimePassword").GetString()!;
        Assert.Equal(1, body.GetProperty("row").GetProperty("indexNumber").GetInt32());
        Assert.Equal("Active", body.GetProperty("row").GetProperty("status").GetString());

        var second = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, NewPhone(), "Nuwan"));
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("row").GetProperty("indexNumber").GetInt32());

        // The phone number is the username until the student picks one, and the one-time password signs them in.
        Assert.Equal(HttpStatusCode.OK, await LoginStatus(phone, otp));

        var list = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}");
        Assert.Equal(2, list.GetArrayLength());
    }

    [Fact]
    public async Task Search_finds_a_student_by_name_index_number_or_phone()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var phone = NewPhone();
        await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, phone, "Amara"));
        await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, NewPhone(), "Bandula"));

        var byName = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&q=amar");
        var byIndex = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&q=2");
        var byPhone = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&q={phone[^6..]}");

        Assert.Equal(["Amara"], byName.EnumerateArray().Select(r => r.GetProperty("firstName").GetString()));
        Assert.Equal(["Bandula"], byIndex.EnumerateArray().Select(r => r.GetProperty("firstName").GetString()));
        Assert.Equal(["Amara"], byPhone.EnumerateArray().Select(r => r.GetProperty("firstName").GetString()));
    }

    [Fact]
    public async Task A_phone_number_already_in_use_offers_to_enrol_that_student_instead_of_a_duplicate()
    {
        var teacher = await factory.NewTeacherAsync();
        var maths = await ClassFor(teacher.Client);
        var science = await ClassFor(teacher.Client, "2027 O/L Science");
        var phone = NewPhone();
        await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(maths, phone));

        var again = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(science, phone));
        Assert.Equal((HttpStatusCode.Conflict, "student_exists"), await Code(again));
        var existing = (await Json(again)).GetProperty("existing");
        Assert.Equal("Kasun", existing.GetProperty("firstName").GetString());
        Assert.Equal("P", existing.GetProperty("lastInitial").GetString());
        Assert.False(existing.GetProperty("enrolledHere").GetBoolean());

        var enrol = await teacher.Client.PostAsJsonAsync("/api/students", new
        {
            phone, firstName = "Ignored", lastName = "Ignored", classId = science, feePlan = "FreeCard", enrolExisting = true,
        });
        Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
        var body = await enrol.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Kasun", body.GetProperty("row").GetProperty("firstName").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("oneTimePassword").ValueKind);

        var studentId = body.GetProperty("row").GetProperty("studentId").GetGuid();
        var detail = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}");
        Assert.Equal(2, detail.GetProperty("enrolments").GetArrayLength());

        var twice = await teacher.Client.PostAsJsonAsync("/api/students", new { phone, classId = science, firstName = "x", lastName = "y", enrolExisting = true });
        Assert.Equal((HttpStatusCode.Conflict, "already_enrolled"), await Code(twice));
    }

    [Fact]
    public async Task A_teacher_sees_only_students_in_their_own_classes()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Kamala");
        var mine = await ClassFor(teacher.Client);
        var theirs = await ClassFor(other.Client);
        var phone = NewPhone();
        var created = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(mine, phone));
        var studentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("row").GetProperty("studentId").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/students/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/students/{studentId}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsJsonAsync($"/api/students/{studentId}", new { firstName = "Hacked", lastName = "Name" })).StatusCode);
        Assert.Empty((await other.Client.GetFromJsonAsync<JsonElement>("/api/students")).EnumerateArray());

        // The other teacher can add the same phone to their class, and learns only a first name and an initial.
        var clash = await other.Client.PostAsJsonAsync("/api/students", StudentBody(theirs, phone));
        Assert.Equal((HttpStatusCode.Conflict, "student_exists"), await Code(clash));
        var existing = (await Json(clash)).GetProperty("existing");
        Assert.False(existing.TryGetProperty("phone", out _));

        // And cannot put their student into someone else's class.
        var wrongClass = await other.Client.PostAsJsonAsync("/api/students", StudentBody(mine, NewPhone()));
        Assert.Equal((HttpStatusCode.BadRequest, "class_required"), await Code(wrongClass));
    }

    [Fact]
    public async Task Bad_input_is_rejected_with_a_code_for_the_field()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client, fee: 1000m);

        async Task<(HttpStatusCode, string?)> Add(object body) => await Code(await teacher.Client.PostAsJsonAsync("/api/students", body));

        Assert.Equal((HttpStatusCode.BadRequest, "phone_invalid"), await Add(StudentBody(classId, "123")));
        Assert.Equal((HttpStatusCode.BadRequest, "first_name_required"), await Add(StudentBody(classId, NewPhone(), " ")));
        Assert.Equal((HttpStatusCode.BadRequest, "class_required"), await Add(StudentBody(Guid.NewGuid(), NewPhone())));
        Assert.Equal((HttpStatusCode.BadRequest, "guardian_phone_invalid"),
            await Add(new { phone = NewPhone(), firstName = "A", lastName = "B", classId, guardianPhone = "abc" }));
        Assert.Equal((HttpStatusCode.BadRequest, "fee_plan_invalid"),
            await Add(new { phone = NewPhone(), firstName = "A", lastName = "B", classId, feePlan = "Gold" }));
        Assert.Equal((HttpStatusCode.BadRequest, "discount_invalid"),
            await Add(new { phone = NewPhone(), firstName = "A", lastName = "B", classId, feePlan = "Discount", discountAmount = 5000 }));
        Assert.Equal((HttpStatusCode.Created, (string?)null),
            await Add(new { phone = NewPhone(), firstName = "A", lastName = "B", classId, feePlan = "Discount", discountAmount = 400 }));

        await Add(new { phone = NewPhone(), firstName = "C", lastName = "D", classId, indexNumber = 7 });
        Assert.Equal((HttpStatusCode.Conflict, "index_taken"), await Add(new { phone = NewPhone(), firstName = "E", lastName = "F", classId, indexNumber = 7 }));

        await teacher.Client.PostAsync($"/api/classes/classes/{classId}/archive", null);
        Assert.Equal((HttpStatusCode.Conflict, "class_archived"), await Add(StudentBody(classId, NewPhone())));
    }

    [Fact]
    public async Task Enrolment_details_can_change_and_a_student_who_left_can_come_back()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client, fee: 3000m);
        var phone = NewPhone();
        var created = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, phone));
        var row = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("row");
        var enrolmentId = row.GetProperty("enrolmentId").GetGuid();
        var other = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, NewPhone(), "Other"));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);

        var changed = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{enrolmentId}",
            new { indexNumber = 12, feePlan = "Discount", discountAmount = 500 });
        var after = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(12, after.GetProperty("indexNumber").GetInt32());
        Assert.Equal("Discount", after.GetProperty("feePlan").GetString());
        Assert.Equal(500m, after.GetProperty("discountAmount").GetDecimal());

        var taken = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{enrolmentId}", new { indexNumber = 2 });
        Assert.Equal((HttpStatusCode.Conflict, "index_taken"), await Code(taken));

        await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{enrolmentId}", new { status = "Left" });
        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&q=Kasun")).EnumerateArray());
        Assert.Single((await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&status=left")).EnumerateArray());

        var back = await teacher.Client.PostAsJsonAsync("/api/students", new { phone, firstName = "x", lastName = "y", classId, enrolExisting = true });
        Assert.Equal(HttpStatusCode.Created, back.StatusCode);
        Assert.Equal("Active", (await back.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("row").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Teacher_resets_a_students_password_and_edits_their_details()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var phone = NewPhone();
        var created = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, phone));
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var studentId = body.GetProperty("row").GetProperty("studentId").GetGuid();
        var oldOtp = body.GetProperty("oneTimePassword").GetString()!;

        var reset = await teacher.Client.PostAsync($"/api/students/{studentId}/reset-password", null);
        var newOtp = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("oneTimePassword").GetString()!;
        Assert.NotEqual(oldOtp, newOtp);
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatus(phone, oldOtp));
        Assert.Equal(HttpStatusCode.OK, await LoginStatus(phone, newOtp));

        var edit = await teacher.Client.PutAsJsonAsync($"/api/students/{studentId}", new { firstName = "Kasuni", lastName = "Perera", school = "Visakha", grade = "Grade 10" });
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var detail = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students/{studentId}");
        Assert.Equal("Kasuni", detail.GetProperty("firstName").GetString());
        Assert.Equal("Visakha", detail.GetProperty("school").GetString());
        Assert.Equal(phone.StartsWith("0") ? "+94" + phone[1..] : phone, detail.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task A_teachers_own_account_is_never_reset_through_the_student_list()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var colleague = await factory.NewTeacherAsync("Colleague");
        var colleaguePhone = await PhoneOf(colleague);

        // The colleague's phone belongs to a Teacher account; adding it as a student links the Student role to it.
        var added = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, colleaguePhone));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var studentId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("row").GetProperty("studentId").GetGuid();

        var reset = await teacher.Client.PostAsync($"/api/students/{studentId}/reset-password", null);
        Assert.Equal((HttpStatusCode.Conflict, "account_not_resettable"), await Code(reset));
    }

    private async Task<string> PhoneOf(TestTeacher teacher)
    {
        var admin = await factory.AdminClientAsync();
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/identity/teachers");
        return all.EnumerateArray().First(t => t.GetProperty("id").GetGuid() == teacher.Id).GetProperty("phone").GetString()!;
    }

    private async Task<(string Token, Guid Id)> InviteFor(HttpClient teacher, Guid classId, object? extra = null)
    {
        var created = await teacher.PostAsJsonAsync("/api/students/invites", extra ?? new { classId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("token").GetString()!, body.GetProperty("id").GetGuid());
    }

    private object Registration(string phone, string password = "Student-pass-1") => new
    {
        phone, firstName = "Dilani", lastName = "Silva", school = "Girls High", grade = "Grade 9", password,
    };

    [Fact]
    public async Task A_student_registers_through_an_invite_link_and_waits_for_approval()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client, "Grade 9 Science");
        var (token, _) = await InviteFor(teacher.Client, classId);
        var anonymous = factory.CreateClient();

        var info = await anonymous.GetFromJsonAsync<JsonElement>($"/api/students/register/{token}");
        Assert.Equal("Grade 9 Science", info.GetProperty("classTitle").GetString());
        Assert.Contains("Nimal", info.GetProperty("teacherName").GetString());

        var phone = NewPhone();
        var registered = await anonymous.PostAsJsonAsync($"/api/students/register/{token}", Registration(phone));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal("created", (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());

        // Their own password works straight away; the teacher still has to approve the class.
        Assert.Equal(HttpStatusCode.OK, await LoginStatus(phone, "Student-pass-1"));
        var pending = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&status=pending");
        var row = Assert.Single(pending.EnumerateArray());
        Assert.Equal("Dilani", row.GetProperty("firstName").GetString());
        Assert.Equal("Pending", row.GetProperty("status").GetString());

        var approved = await teacher.Client.PostAsync($"/api/students/enrolments/{row.GetProperty("enrolmentId").GetGuid()}/approve", null);
        Assert.Equal("Active", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&status=pending")).EnumerateArray());
    }

    [Fact]
    public async Task Rejecting_a_request_removes_it_and_approving_twice_is_refused()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var (token, _) = await InviteFor(teacher.Client, classId);
        await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration(NewPhone()));
        var pending = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&status=pending");
        var id = Assert.Single(pending.EnumerateArray()).GetProperty("enrolmentId").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.PostAsync($"/api/students/enrolments/{id}/reject", null)).StatusCode);
        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&status=all")).EnumerateArray());

        await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration(NewPhone()));
        var next = (await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={classId}&status=pending")).EnumerateArray().Single().GetProperty("enrolmentId").GetGuid();
        await teacher.Client.PostAsync($"/api/students/enrolments/{next}/approve", null);
        Assert.Equal((HttpStatusCode.Conflict, "not_pending"), await Code(await teacher.Client.PostAsync($"/api/students/enrolments/{next}/approve", null)));
    }

    [Fact]
    public async Task A_link_stops_working_when_full_or_revoked_and_an_unknown_link_is_not_found()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var anonymous = factory.CreateClient();

        var (single, _) = await InviteFor(teacher.Client, classId, new { classId, maxUses = 1 });
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync($"/api/students/register/{single}", Registration(NewPhone()))).StatusCode);
        Assert.Equal((HttpStatusCode.Gone, "invite_full"), await Code(await anonymous.PostAsJsonAsync($"/api/students/register/{single}", Registration(NewPhone()))));
        Assert.Equal((HttpStatusCode.Gone, "invite_full"), await Code(await anonymous.GetAsync($"/api/students/register/{single}")));

        var (token, inviteId) = await InviteFor(teacher.Client, classId);
        var revoked = await teacher.Client.PostAsync($"/api/students/invites/{inviteId}/revoke", null);
        Assert.Equal("Revoked", (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.Gone, "invite_revoked"), await Code(await anonymous.GetAsync($"/api/students/register/{token}")));

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/students/register/not-a-real-link")).StatusCode);

        var links = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students/invites?classId={classId}");
        Assert.Equal(["Revoked", "Full"], links.EnumerateArray().Select(l => l.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Registering_with_a_phone_that_has_an_account_never_changes_that_account()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var phone = NewPhone();
        var added = await teacher.Client.PostAsJsonAsync("/api/students", StudentBody(classId, phone));
        var otp = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("oneTimePassword").GetString()!;

        var science = await ClassFor(teacher.Client, "Science");
        var (token, _) = await InviteFor(teacher.Client, science);
        var result = await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration(phone, "Attacker-pass-1"));
        Assert.Equal("existing_account", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());

        // The chosen password was thrown away: the one-time password still works and the new one does not.
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatus(phone, "Attacker-pass-1"));
        Assert.Equal(HttpStatusCode.OK, await LoginStatus(phone, otp));
        var pending = await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={science}&status=pending");
        Assert.Equal("Kasun", Assert.Single(pending.EnumerateArray()).GetProperty("firstName").GetString());

        // Asking again for the same class gives the same answer and adds nothing.
        var again = await factory.CreateClient().PostAsJsonAsync($"/api/students/register/{token}", Registration(phone));
        Assert.Equal("existing_account", (await Json(again)).GetProperty("outcome").GetString());
        Assert.Single((await teacher.Client.GetFromJsonAsync<JsonElement>($"/api/students?classId={science}&status=all")).EnumerateArray());
    }

    [Fact]
    public async Task Registration_checks_the_form_and_a_teachers_phone_cannot_be_claimed()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassFor(teacher.Client);
        var (token, _) = await InviteFor(teacher.Client, classId);
        var anonymous = factory.CreateClient();
        string url = $"/api/students/register/{token}";

        Assert.Equal((HttpStatusCode.BadRequest, "password_too_short"), await Code(await anonymous.PostAsJsonAsync(url, Registration(NewPhone(), "short"))));
        Assert.Equal((HttpStatusCode.BadRequest, "phone_invalid"), await Code(await anonymous.PostAsJsonAsync(url, Registration("12"))));
        Assert.Equal((HttpStatusCode.Conflict, "phone_taken"), await Code(await anonymous.PostAsJsonAsync(url, Registration(await PhoneOf(teacher)))));
    }

    [Fact]
    public async Task Invite_links_are_for_the_teachers_own_open_classes_and_teacher_pages_need_sign_in()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Kamala");
        var classId = await ClassFor(teacher.Client);

        var foreign = await other.Client.PostAsJsonAsync("/api/students/invites", new { classId });
        Assert.Equal((HttpStatusCode.BadRequest, "class_required"), await Code(foreign));
        Assert.Equal((HttpStatusCode.BadRequest, "invite_days_invalid"), await Code(await teacher.Client.PostAsJsonAsync("/api/students/invites", new { classId, expiresInDays = 0 })));
        Assert.Equal((HttpStatusCode.BadRequest, "invite_uses_invalid"), await Code(await teacher.Client.PostAsJsonAsync("/api/students/invites", new { classId, maxUses = 0 })));

        var (_, id) = await InviteFor(teacher.Client, classId);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/students/invites/{id}/revoke", null)).StatusCode);
        Assert.Empty((await other.Client.GetFromJsonAsync<JsonElement>("/api/students/invites")).EnumerateArray());

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/students/invites")).StatusCode);
    }
}
