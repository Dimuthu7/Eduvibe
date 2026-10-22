using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GuruBook.Api.Tests;

/// <summary>Student profile photos: who may set one, what is accepted, and what the teacher's list and the privacy export say.</summary>
public class StudentPhotosApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 5000;

    private static string NewPhone() => $"075{Interlocked.Increment(ref _phoneCounter):D7}";

    // The first bytes of a JPEG, a PNG and a WebP, then filler: only the signature is read.
    private static byte[] Jpeg(int size = 2000) => Pad([0xFF, 0xD8, 0xFF, 0xE0], size);

    private static byte[] Png() => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 100);

    private static byte[] Webp() => Pad("RIFF\0\0\0\0WEBP"u8.ToArray(), 100);

    private static byte[] Pad(byte[] head, int size)
    {
        var bytes = new byte[size];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    private static ByteArrayContent Body(byte[] bytes, string type = "image/jpeg")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> ClassOf(HttpClient teacher)
    {
        var venueId = (await Json(await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Hall", district = "Kandy" }))).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title = "Photo class", subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2000m, slots = new[] { new { day = 2, start = "16:00", end = "18:00" } },
        });
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private sealed record Enrolled(Guid StudentId, Guid EnrolmentId, string Phone, string Otp);

    private static async Task<Enrolled> Enrol(HttpClient teacher, Guid classId, string first)
    {
        var phone = NewPhone();
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Photo", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var row = body.GetProperty("row");
        return new Enrolled(row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), phone, body.GetProperty("oneTimePassword").GetString()!);
    }

    private async Task<HttpClient> SignInStudent(Enrolled student)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = student.Phone, password = student.Otp });
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = student.Otp, newPassword = "Student-pass-1", username = "p" + Guid.NewGuid().ToString("N")[..10] });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);
        return client;
    }

    [Fact]
    public async Task A_teacher_can_set_replace_and_remove_a_photo_and_the_list_says_who_has_one()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassOf(teacher.Client);
        var kamal = await Enrol(teacher.Client, classId, "Kamal");
        await Enrol(teacher.Client, classId, "Nimali");

        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>("/api/students/photos")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.Client.GetAsync($"/api/students/{kamal.StudentId}/photo")).StatusCode);

        var put = await teacher.Client.PutAsync($"/api/students/{kamal.StudentId}/photo", Body(Jpeg()));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var first = await Json(put);
        Assert.Equal(kamal.StudentId, first.GetProperty("studentId").GetGuid());
        Assert.Equal(kamal.EnrolmentId, first.GetProperty("enrolmentIds")[0].GetGuid());

        var list = await teacher.Client.GetFromJsonAsync<JsonElement>("/api/students/photos");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(first.GetProperty("version").GetInt64(), list[0].GetProperty("version").GetInt64());

        var image = await teacher.Client.GetAsync($"/api/students/{kamal.StudentId}/photo");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2000, (await image.Content.ReadAsByteArrayAsync()).Length);

        // A second upload replaces the first and moves the version on, so phones fetch the new picture.
        await Task.Delay(20);
        var replaced = await Json(await teacher.Client.PutAsync($"/api/students/{kamal.StudentId}/photo", Body(Png())));
        Assert.True(replaced.GetProperty("version").GetInt64() > first.GetProperty("version").GetInt64());
        Assert.Equal(1, (await teacher.Client.GetFromJsonAsync<JsonElement>("/api/students/photos")).GetArrayLength());
        Assert.Equal("image/png", (await teacher.Client.GetAsync($"/api/students/{kamal.StudentId}/photo")).Content.Headers.ContentType?.MediaType);
        Assert.Equal("image/webp", (await PutThenType(teacher.Client, kamal.StudentId, Webp())));

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/students/{kamal.StudentId}/photo")).StatusCode);
        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>("/api/students/photos")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync($"/api/students/{kamal.StudentId}/photo")).StatusCode);
    }

    private static async Task<string?> PutThenType(HttpClient teacher, Guid studentId, byte[] bytes)
    {
        await teacher.PutAsync($"/api/students/{studentId}/photo", Body(bytes));
        return (await teacher.GetAsync($"/api/students/{studentId}/photo")).Content.Headers.ContentType?.MediaType;
    }

    [Fact]
    public async Task Only_images_of_a_sensible_size_are_kept_and_the_type_comes_from_the_bytes()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await Enrol(teacher.Client, await ClassOf(teacher.Client), "Sahan");
        var url = $"/api/students/{student.StudentId}/photo";

        var notAnImage = await teacher.Client.PutAsync(url, Body("<html>not a picture</html>"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, notAnImage.StatusCode);
        Assert.Equal("photo_invalid", (await Json(notAnImage)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.Client.PutAsync(url, Body([]))).StatusCode);

        var tooBig = await teacher.Client.PutAsync(url, Body(Jpeg(300 * 1024)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Equal("photo_too_large", (await Json(tooBig)).GetProperty("code").GetString());

        // A request that says it is text but carries a JPEG is served as a JPEG, and one that says JPEG over a script is refused.
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PutAsync(url, Body(Jpeg(), "text/plain"))).StatusCode);
        Assert.Equal("image/jpeg", (await teacher.Client.GetAsync(url)).Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.Client.PutAsync(url, Body("<script>alert(1)</script>"u8.ToArray(), "image/jpeg"))).StatusCode);
    }

    [Fact]
    public async Task A_teacher_can_only_reach_the_photos_of_their_own_students()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Kamala");
        var student = await Enrol(teacher.Client, await ClassOf(teacher.Client), "Mine");
        await teacher.Client.PutAsync($"/api/students/{student.StudentId}/photo", Body(Jpeg()));
        var url = $"/api/students/{student.StudentId}/photo";

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsync(url, Body(Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.DeleteAsync(url)).StatusCode);
        Assert.Empty((await other.Client.GetFromJsonAsync<JsonElement>("/api/students/photos")).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/students/photos")).StatusCode);
    }

    [Fact]
    public async Task A_student_sees_their_own_photo_and_nobody_elses_and_cannot_change_it()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassOf(teacher.Client);
        var amara = await Enrol(teacher.Client, classId, "Amara");
        var bimal = await Enrol(teacher.Client, classId, "Bimal");
        await teacher.Client.PutAsync($"/api/students/{amara.StudentId}/photo", Body(Jpeg(1234)));
        var student = await SignInStudent(amara);
        var bimalClient = await SignInStudent(bimal);

        var mine = await student.GetAsync("/api/students/me/photo");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal(1234, (await mine.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(HttpStatusCode.NoContent, (await bimalClient.GetAsync("/api/students/me/photo")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/students/{amara.StudentId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsync($"/api/students/{amara.StudentId}/photo", Body(Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.DeleteAsync($"/api/students/{amara.StudentId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/students/me/photo")).StatusCode);
    }

    [Fact]
    public async Task The_privacy_export_includes_the_photo_and_erasing_the_student_removes_it()
    {
        var teacher = await factory.NewTeacherAsync("Pictura");
        var admin = await factory.AdminClientAsync();
        var student = await Enrol(teacher.Client, await ClassOf(teacher.Client), "Faceful");
        await teacher.Client.PutAsync($"/api/students/{student.StudentId}/photo", Body(Jpeg(300)));
        var base64 = Convert.ToBase64String(Jpeg(300));

        var teacherExport = await (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).Content.ReadAsStringAsync();
        Assert.Contains("student_photos", teacherExport);
        Assert.Contains(base64, teacherExport);

        var studentExport = await (await admin.GetAsync($"/api/privacy/students/{student.StudentId}/export")).Content.ReadAsStringAsync();
        Assert.Contains("student_photos", studentExport);
        Assert.Contains(base64, studentExport);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/privacy/students/{student.StudentId}/erase", new { confirm = "Faceful Photo" })).StatusCode);
        Assert.Empty((await teacher.Client.GetFromJsonAsync<JsonElement>("/api/students/photos")).EnumerateArray());
        Assert.DoesNotContain("student_photos", await (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Erasing_a_teacher_removes_the_photos_of_students_nobody_else_teaches_but_keeps_shared_ones()
    {
        var teacher = await factory.NewTeacherAsync("Eraseme");
        var other = await factory.NewTeacherAsync("Stayhere");
        var admin = await factory.AdminClientAsync();
        var alone = await Enrol(teacher.Client, await ClassOf(teacher.Client), "Solo");
        var shared = await Enrol(teacher.Client, await ClassOf(teacher.Client), "Both");
        var enrolled = await other.Client.PostAsJsonAsync("/api/students", new
        {
            phone = shared.Phone, firstName = "Both", lastName = "Photo", classId = await ClassOf(other.Client), feePlan = "Full", enrolExisting = true,
        });
        Assert.Equal(HttpStatusCode.Created, enrolled.StatusCode);
        await teacher.Client.PutAsync($"/api/students/{alone.StudentId}/photo", Body(Jpeg()));
        await teacher.Client.PutAsync($"/api/students/{shared.StudentId}/photo", Body(Jpeg()));

        Assert.True((await admin.PutAsJsonAsync($"/api/identity/teachers/{teacher.Id}/active", new { isActive = false })).IsSuccessStatusCode);
        var erased = await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "Eraseme Teacher" });
        Assert.True(erased.IsSuccessStatusCode, await erased.Content.ReadAsStringAsync());

        var left = await other.Client.GetFromJsonAsync<JsonElement>("/api/students/photos");
        Assert.Equal(1, left.GetArrayLength());
        Assert.Equal(shared.StudentId, left[0].GetProperty("studentId").GetGuid());
    }
}
