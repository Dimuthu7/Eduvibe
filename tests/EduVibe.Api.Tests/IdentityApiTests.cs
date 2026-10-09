using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EduVibe.Api.Tests;

public class IdentityApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record User(Guid Id, string Phone, string FullName, string[] Roles, bool MustChangePassword, Guid? TeacherId);
    private record Session(string AccessToken, string RefreshToken, User User);
    private record Teacher(Guid Id, string Phone, bool IsActive);
    private record Created(Teacher Teacher, string OneTimePassword);
    private record Institute(Guid Id, string Name, Guid[] TeacherIds);

    private static int _phoneCounter = 100;
    private static string NewPhone() => $"+9477{Interlocked.Increment(ref _phoneCounter):D7}";

    private HttpClient Anonymous() => factory.CreateClient();

    private static HttpClient As(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<Session> Login(string phone, string password)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/identity/login", new { phone, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Session>(Json))!;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Task<HttpClient> AdminClient() => factory.AdminClientAsync();

    private async Task<Session> ChangePassword(Session session, string current, string next)
    {
        var client = As(factory.CreateClient(), session.AccessToken);
        var response = await client.PostAsJsonAsync("/api/identity/change-password", new { currentPassword = current, newPassword = next });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Session>(Json))!;
    }

    private static async Task<Created> CreateTeacher(HttpClient admin, string phone, string name = "Nimal Perera")
    {
        var response = await admin.PostAsJsonAsync("/api/identity/teachers", new { fullName = name, phone, town = "Kandy", subjects = "Maths" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Json))!;
    }

    [Fact]
    public async Task Wrong_password_and_unknown_phone_get_the_same_answer()
    {
        var wrong = await Anonymous().PostAsJsonAsync("/api/identity/login", new { phone = ApiFactory.AdminPhone, password = "nope-nope" });
        var unknown = await Anonymous().PostAsJsonAsync("/api/identity/login", new { phone = "+94779999999", password = "nope-nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_need_a_token()
    {
        var response = await Anonymous().GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task One_time_password_must_be_replaced_before_anything_else_works()
    {
        var first = await Login(ApiFactory.FreshAdminPhone, ApiFactory.AdminPassword);
        Assert.True(first.User.MustChangePassword);
        var client = As(factory.CreateClient(), first.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/identity/teachers")).StatusCode);

        var changed = await ChangePassword(first, ApiFactory.AdminPassword, ApiFactory.AdminPassword + "x");
        Assert.False(changed.User.MustChangePassword);
        var after = As(factory.CreateClient(), changed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await after.GetAsync("/api/identity/teachers")).StatusCode);
    }

    [Fact]
    public async Task Super_admin_creates_a_teacher_who_signs_in_with_the_one_time_password()
    {
        var admin = await AdminClient();
        var phone = NewPhone();
        var created = await CreateTeacher(admin, phone);

        // The teacher can type the number the local way.
        var local = "0" + phone[3..];
        var session = await Login(local, created.OneTimePassword);

        Assert.Equal(phone, session.User.Phone);
        Assert.Equal(["Teacher"], session.User.Roles);
        Assert.True(session.User.MustChangePassword);
        Assert.Equal(created.Teacher.Id, session.User.TeacherId);

        var changed = await ChangePassword(session, created.OneTimePassword, "My-new-pass-1");
        var teacher = As(factory.CreateClient(), changed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/api/identity/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/identity/teachers")).StatusCode);
    }

    [Fact]
    public async Task Teacher_can_edit_own_profile()
    {
        var admin = await AdminClient();
        var created = await CreateTeacher(admin, NewPhone());
        var session = await ChangePassword(await Login(created.Teacher.Phone, created.OneTimePassword), created.OneTimePassword, "My-new-pass-1");
        var teacher = As(factory.CreateClient(), session.AccessToken);

        var response = await teacher.PutAsJsonAsync("/api/identity/me",
            new { fullName = "Nimal P.", email = "nimal@example.com", language = "si", town = "Galle", subjects = "Science" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await teacher.GetFromJsonAsync<JsonElement>("/api/identity/me");
        Assert.Equal("Nimal P.", me.GetProperty("fullName").GetString());
        Assert.Equal("si", me.GetProperty("language").GetString());
        Assert.Equal("Galle", me.GetProperty("town").GetString());
    }

    [Fact]
    public async Task A_phone_number_can_only_be_used_once()
    {
        var admin = await AdminClient();
        var phone = NewPhone();
        await CreateTeacher(admin, phone);

        var again = await admin.PostAsJsonAsync("/api/identity/teachers", new { fullName = "Other", phone = "0" + phone[3..] });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Invalid_phone_is_rejected()
    {
        var admin = await AdminClient();

        var response = await admin.PostAsJsonAsync("/api/identity/teachers", new { fullName = "Bad Phone", phone = "12345" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_a_while()
    {
        var admin = await AdminClient();
        var created = await CreateTeacher(admin, NewPhone());

        for (var i = 0; i < 5; i++)
        {
            var bad = await Anonymous().PostAsJsonAsync("/api/identity/login", new { phone = created.Teacher.Phone, password = "wrong-pass" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        var locked = await Anonymous().PostAsJsonAsync("/api/identity/login", new { phone = created.Teacher.Phone, password = created.OneTimePassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_a_reused_token_ends_the_session()
    {
        var admin = await AdminClient();
        var created = await CreateTeacher(admin, NewPhone());
        var session = await Login(created.Teacher.Phone, created.OneTimePassword);

        var refreshed = await Anonymous().PostAsJsonAsync("/api/identity/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var next = (await refreshed.Content.ReadFromJsonAsync<Session>(Json))!;
        Assert.NotEqual(session.RefreshToken, next.RefreshToken);

        var reused = await Anonymous().PostAsJsonAsync("/api/identity/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

        var nextAfterReuse = await Anonymous().PostAsJsonAsync("/api/identity/refresh", new { refreshToken = next.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, nextAfterReuse.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var admin = await AdminClient();
        var created = await CreateTeacher(admin, NewPhone());
        var session = await Login(created.Teacher.Phone, created.OneTimePassword);

        var logout = await Anonymous().PostAsJsonAsync("/api/identity/logout", new { refreshToken = session.RefreshToken });
        var refresh = await Anonymous().PostAsJsonAsync("/api/identity/refresh", new { refreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Reset_password_gives_a_new_one_time_password_and_ends_old_sessions()
    {
        var admin = await AdminClient();
        var created = await CreateTeacher(admin, NewPhone());
        var session = await Login(created.Teacher.Phone, created.OneTimePassword);

        var reset = await admin.PostAsync($"/api/identity/teachers/{created.Teacher.Id}/reset-password", null);
        var otp = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("oneTimePassword").GetString()!;

        var oldRefresh = await Anonymous().PostAsJsonAsync("/api/identity/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        var fresh = await Login(created.Teacher.Phone, otp);
        Assert.True(fresh.User.MustChangePassword);
    }

    [Fact]
    public async Task Deactivated_teacher_cannot_sign_in()
    {
        var admin = await AdminClient();
        var created = await CreateTeacher(admin, NewPhone());

        var off = await admin.PutAsJsonAsync($"/api/identity/teachers/{created.Teacher.Id}/active", new { isActive = false });
        var login = await Anonymous().PostAsJsonAsync("/api/identity/login", new { phone = created.Teacher.Phone, password = created.OneTimePassword });

        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Institutes_are_managed_by_super_admin_and_seen_by_their_teachers()
    {
        var admin = await AdminClient();
        var linked = await CreateTeacher(admin, NewPhone());
        var other = await CreateTeacher(admin, NewPhone(), "Kamala Silva");

        var created = await admin.PostAsJsonAsync("/api/classes/institutes", new { name = "Bright Minds", town = "Kandy" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var institute = (await created.Content.ReadFromJsonAsync<Institute>(Json))!;

        var assign = await admin.PutAsJsonAsync($"/api/classes/institutes/{institute.Id}/teachers", new { teacherIds = new[] { linked.Teacher.Id } });
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var unknown = await admin.PutAsJsonAsync($"/api/classes/institutes/{institute.Id}/teachers", new { teacherIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var teacherSession = await ChangePassword(await Login(linked.Teacher.Phone, linked.OneTimePassword), linked.OneTimePassword, "My-new-pass-1");
        var mine = await As(factory.CreateClient(), teacherSession.AccessToken).GetFromJsonAsync<Institute[]>("/api/classes/my-institutes", Json);
        Assert.Equal(["Bright Minds"], mine!.Select(i => i.Name));

        var otherSession = await ChangePassword(await Login(other.Teacher.Phone, other.OneTimePassword), other.OneTimePassword, "My-new-pass-1");
        var theirs = await As(factory.CreateClient(), otherSession.AccessToken).GetFromJsonAsync<Institute[]>("/api/classes/my-institutes", Json);
        Assert.Empty(theirs!);

        var forbidden = await As(factory.CreateClient(), teacherSession.AccessToken).GetAsync("/api/classes/institutes");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
