using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduVibe.Shared.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduVibe.Api.Tests;

/// <summary>Handing over and erasing a teacher's or a student's data, which only the Super Admin can do.</summary>
public class PrivacyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 7_700_000;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private static string NewPhone() => $"07{Interlocked.Increment(ref _phones):D8}";

    private async Task<Guid> ClassOf(HttpClient teacher, string title)
    {
        var venueId = (await Json(await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Hall", district = "Kandy" }))).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = 2000m, slots = new[] { new { day = 2, start = "16:00", end = "18:00" } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private sealed record Enrolled(Guid StudentId, Guid EnrolmentId, string Phone, string Otp);

    private static async Task<Enrolled> Enrol(HttpClient teacher, Guid classId, string first, string? phone = null)
    {
        var existing = phone is not null;
        phone ??= NewPhone();
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Perera", classId, feePlan = "Full", enrolExisting = existing });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var row = body.GetProperty("row");
        var otp = body.TryGetProperty("oneTimePassword", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
        return new Enrolled(row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), phone, otp);
    }

    private async Task<HttpStatusCode> StudentLogin(Enrolled student) =>
        (await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = student.Phone, password = student.Otp })).StatusCode;

    private async Task Fill(HttpClient teacher, Enrolled student)
    {
        var paid = await teacher.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = student.EnrolmentId, amount = 500m, method = "Cash", paidOn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)).ToString("yyyy-MM-dd"), months = Array.Empty<string>() });
        Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
    }

    private async Task Deactivate(HttpClient admin, Guid teacherId) =>
        Assert.True((await admin.PutAsJsonAsync($"/api/identity/teachers/{teacherId}/active", new { isActive = false })).IsSuccessStatusCode);

    private async Task<int> AuditRowsHolding(Guid tenantId, string text)
    {
        using var scope = factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await audit.Entries.Where(e => e.TenantId == tenantId && ((e.Before != null && e.Before.Contains(text)) || (e.After != null && e.After.Contains(text)))).CountAsync();
    }

    [Fact]
    public async Task Only_the_Super_Admin_can_use_the_privacy_endpoints()
    {
        var teacher = await factory.NewTeacherAsync();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync($"/api/privacy/students/{id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync($"/api/privacy/students/{id}/erase", new { confirm = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).StatusCode);
    }

    [Fact]
    public async Task A_teacher_export_holds_their_classes_students_and_fees_but_no_secrets()
    {
        var teacher = await factory.NewTeacherAsync("Exporta");
        var admin = await factory.AdminClientAsync();
        var classId = await ClassOf(teacher.Client, "Export class");
        var student = await Enrol(teacher.Client, classId, "Kamala");
        await Fill(teacher.Client, student);

        var response = await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Contains("Exporta", text);
        Assert.Contains("Export class", text);
        Assert.Contains("Kamala", text);
        Assert.Contains(student.Phone[1..], text);
        Assert.Contains("500", text);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", text);
        Assert.DoesNotContain(student.Otp, text);
        Assert.DoesNotContain("Teacher-pass-1", text);
    }

    [Fact]
    public async Task Erasing_a_teacher_needs_them_switched_off_and_their_full_name_typed_back()
    {
        var teacher = await factory.NewTeacherAsync("Guarded");
        var admin = await factory.AdminClientAsync();

        Assert.Equal("teacher_still_active", await Code(await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "Guarded Teacher" })));

        await Deactivate(admin, teacher.Id);
        Assert.Equal("confirm_mismatch", await Code(await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "Someone Else" })));
        Assert.Equal("confirm_mismatch", await Code(await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = (string?)null })));
        Assert.Equal("teacher_not_found", await Code(await admin.PostAsJsonAsync($"/api/privacy/teachers/{Guid.NewGuid()}/erase", new { confirm = "x" })));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/privacy/teachers/{Guid.NewGuid()}/export")).StatusCode);

        // Nothing was deleted by the refusals: the export still works.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).StatusCode);
    }

    [Fact]
    public async Task Erasing_a_teacher_removes_their_data_and_students_nobody_else_teaches_but_keeps_other_teachers_work()
    {
        var teacher = await factory.NewTeacherAsync("Leaving");
        var other = await factory.NewTeacherAsync("Staying");
        var admin = await factory.AdminClientAsync();
        var myClass = await ClassOf(teacher.Client, "Leaving class");
        var otherClass = await ClassOf(other.Client, "Staying class");

        var mine = await Enrol(teacher.Client, myClass, "Onlymine");
        var shared = await Enrol(teacher.Client, myClass, "Shared");
        var alsoWithOther = await Enrol(other.Client, otherClass, "Shared", shared.Phone);
        Assert.Equal(shared.StudentId, alsoWithOther.StudentId);
        await Fill(teacher.Client, mine);
        var kept = await Enrol(other.Client, otherClass, "Kept");
        await Fill(other.Client, kept);
        Assert.Equal(HttpStatusCode.OK, await StudentLogin(mine));
        Assert.True(await AuditRowsHolding(teacher.Id, "Onlymine") > 0);

        await Deactivate(admin, teacher.Id);
        var erased = await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "  leaving   TEACHER " });
        Assert.Equal(HttpStatusCode.NoContent, erased.StatusCode);

        // Their own data is gone, and so is the student only they taught, with their sign-in.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/privacy/students/{mine.StudentId}/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await StudentLogin(mine));
        Assert.Equal(0, await AuditRowsHolding(teacher.Id, "Onlymine"));
        Assert.Equal(0, await AuditRowsHolding(teacher.Id, "Leaving class"));

        // The student another teacher also teaches stays, with that teacher's records.
        var sharedExport = await admin.GetAsync($"/api/privacy/students/{shared.StudentId}/export");
        Assert.Equal(HttpStatusCode.OK, sharedExport.StatusCode);
        Assert.Contains("Staying class", await sharedExport.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Leaving class", await (await admin.GetAsync($"/api/privacy/students/{shared.StudentId}/export")).Content.ReadAsStringAsync());

        // The other teacher is untouched.
        var otherText = await (await admin.GetAsync($"/api/privacy/teachers/{other.Id}/export")).Content.ReadAsStringAsync();
        Assert.Contains("Staying class", otherText);
        Assert.Contains("Kept", otherText);
        Assert.Contains("Staying class", await other.Client.GetStringAsync("/api/classes/classes"));

        // The trail records that the erasure happened, and says nothing about the person.
        Assert.True(await AuditRowsHolding(teacher.Id, "erased") > 0);

        // Erasing again finds nobody and is harmless.
        Assert.Equal("teacher_not_found", await Code(await admin.PostAsJsonAsync($"/api/privacy/teachers/{teacher.Id}/erase", new { confirm = "Leaving Teacher" })));
    }

    [Fact]
    public async Task A_student_export_covers_every_teacher_they_study_with()
    {
        var first = await factory.NewTeacherAsync();
        var second = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        var a = await Enrol(first.Client, await ClassOf(first.Client, "First class"), "Both");
        await Enrol(second.Client, await ClassOf(second.Client, "Second class"), "Both", a.Phone);
        await Fill(first.Client, a);

        var response = await admin.GetAsync($"/api/privacy/students/{a.StudentId}/export");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(a.Phone[1..], text);
        Assert.Contains("First class", text);
        Assert.Contains("Second class", text);
        Assert.Contains("500", text);
        Assert.DoesNotContain("PasswordHash", text);
        Assert.Equal("student_not_found", await Code(await admin.GetAsync($"/api/privacy/students/{Guid.NewGuid()}/export")));
    }

    [Fact]
    public async Task A_student_export_includes_what_hangs_off_payments_and_the_void_reason_goes_with_the_student()
    {
        var teacher = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        var classId = await ClassOf(teacher.Client, "Allocation class");
        var student = await Enrol(teacher.Client, classId, "Withdues");
        await teacher.Client.GetAsync($"/api/fees/classes/{classId}"); // makes this month's due
        await Fill(teacher.Client, student);
        var payment = (await Json(await teacher.Client.GetAsync($"/api/fees/enrolments/{student.EnrolmentId}"))).GetProperty("payments").EnumerateArray().First().GetProperty("id").GetGuid();
        await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{payment}/void", new { reason = "Mother Withdues Perera said wrong amount" });

        var text = await (await admin.GetAsync($"/api/privacy/students/{student.StudentId}/export")).Content.ReadAsStringAsync();
        Assert.Contains("payment_allocations", text);
        Assert.Contains("Mother Withdues Perera said wrong amount", text);

        await admin.PostAsJsonAsync($"/api/privacy/students/{student.StudentId}/erase", new { confirm = "Withdues Perera" });

        var after = await (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Withdues", after);
        Assert.Contains("payment_allocations", after);
        Assert.Equal(0, await AuditRowsHolding(teacher.Id, "Mother Withdues"));
    }

    [Fact]
    public async Task Erasing_a_student_removes_who_they_are_but_teachers_keep_their_records()
    {
        var teacher = await factory.NewTeacherAsync();
        var admin = await factory.AdminClientAsync();
        var classId = await ClassOf(teacher.Client, "Records class");
        var student = await Enrol(teacher.Client, classId, "Goneperson");
        await Fill(teacher.Client, student);
        Assert.Equal(HttpStatusCode.OK, await StudentLogin(student));

        Assert.Equal("confirm_mismatch", await Code(await admin.PostAsJsonAsync($"/api/privacy/students/{student.StudentId}/erase", new { confirm = "Goneperson" })));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/privacy/students/{student.StudentId}/erase", new { confirm = "goneperson perera" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await StudentLogin(student));
        var text = await (await admin.GetAsync($"/api/privacy/teachers/{teacher.Id}/export")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Goneperson", text);
        Assert.DoesNotContain(student.Phone[1..], text);
        Assert.Contains("500", text);
        Assert.Equal(0, await AuditRowsHolding(teacher.Id, "Goneperson"));
    }
}
