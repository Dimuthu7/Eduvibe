using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GuruBook.Modules.Fees;
using GuruBook.Modules.Fees.Services;
using GuruBook.Shared.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Api.Tests;

public class FeesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 7000;

    private static string NewPhone() => $"075{Interlocked.Increment(ref _phoneCounter):D7}";

    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    private static string ThisMonth => Today.ToString("yyyy-MM");

    private static string MonthsAhead(int n) => Today.AddMonths(n).ToString("yyyy-MM");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private async Task<Guid> ClassWithFee(HttpClient teacher, decimal fee = 2000m, string title = "2027 A/L Maths")
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home class", district = "Kandy" });
        var venueId = (await Json(venue)).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = fee, slots = new[] { new { day = 1, start = "16:00", end = "18:00" } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddStudent(HttpClient teacher, Guid classId, string first, string plan = "Full", decimal? discount = null)
    {
        var response = await teacher.PostAsJsonAsync("/api/students", new
        {
            phone = NewPhone(), firstName = first, lastName = "Perera", classId, feePlan = plan, discountAmount = discount,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
    }

    private static Task<HttpResponseMessage> Pay(HttpClient teacher, Guid enrolment, decimal amount, string[]? months = null, string method = "Cash", DateOnly? paidOn = null) =>
        teacher.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = enrolment, amount, method, paidOn, months });

    private static async Task<JsonElement> Month(HttpClient teacher, Guid classId, string? month = null) =>
        await Json(await teacher.GetAsync($"/api/fees/classes/{classId}" + (month is null ? "" : $"?month={month}")));

    private static JsonElement Row(JsonElement month, Guid enrolment) =>
        month.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("enrolmentId").GetGuid() == enrolment);

    [Fact]
    public async Task Opening_a_month_bills_each_student_their_plan_and_does_it_only_once()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client, 2000m);
        var full = await AddStudent(teacher.Client, classId, "Full");
        var discount = await AddStudent(teacher.Client, classId, "Disc", "Discount", 500m);
        var free = await AddStudent(teacher.Client, classId, "Free", "FreeCard");

        var month = await Month(teacher.Client, classId);
        Assert.Equal(ThisMonth, month.GetProperty("month").GetString());
        Assert.Equal(2000m, Row(month, full).GetProperty("due").GetDecimal());
        Assert.Equal(1500m, Row(month, discount).GetProperty("due").GetDecimal());
        Assert.Equal(0m, Row(month, free).GetProperty("due").GetDecimal());
        Assert.Equal("FreeCard", Row(month, free).GetProperty("status").GetString());
        Assert.Equal("Unpaid", Row(month, full).GetProperty("status").GetString());

        var again = await Month(teacher.Client, classId);
        Assert.Equal(3, again.GetProperty("rows").GetArrayLength());
        var totals = again.GetProperty("totals");
        Assert.Equal(2, totals.GetProperty("unpaid").GetInt32());
        Assert.Equal(1, totals.GetProperty("freeCard").GetInt32());
        Assert.Equal(3500m, totals.GetProperty("due").GetDecimal());
        Assert.Equal(3500m, totals.GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task The_nightly_job_makes_the_same_dues_for_every_teacher_without_duplicating_them()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client, 1800m);
        var student = await AddStudent(teacher.Client, classId, "Nightly");

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<FeeDueService>();
            await service.EnsureForAllTeachersAsync([ThisMonth], CancellationToken.None);
            await service.EnsureForAllTeachersAsync([ThisMonth], CancellationToken.None);
        }

        var month = await Month(teacher.Client, classId);
        Assert.Equal(1800m, Row(month, student).GetProperty("due").GetDecimal());
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<FeesDbContext>();
        Assert.Equal(1, await db.Dues.IgnoreQueryFilters().CountAsync(d => d.EnrolmentId == student && d.Month == ThisMonth));
    }

    [Fact]
    public async Task An_unpaid_month_follows_a_changed_plan_but_a_part_paid_one_keeps_its_amount()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client, 2000m);
        var untouched = await AddStudent(teacher.Client, classId, "Untouched");
        var paying = await AddStudent(teacher.Client, classId, "Paying");
        await Month(teacher.Client, classId);
        await Pay(teacher.Client, paying, 500m);

        foreach (var enrolment in new[] { untouched, paying })
        {
            var change = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{enrolment}", new { indexNumber = (int?)null, status = "Active", feePlan = "Discount", discountAmount = 1000m });
            Assert.True(change.IsSuccessStatusCode);
        }

        var month = await Month(teacher.Client, classId);
        Assert.Equal(1000m, Row(month, untouched).GetProperty("due").GetDecimal());
        Assert.Equal(2000m, Row(month, paying).GetProperty("due").GetDecimal());
    }

    [Fact]
    public async Task Part_payments_add_up_and_the_status_moves_from_unpaid_to_partly_to_paid()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client, 2000m);
        var student = await AddStudent(teacher.Client, classId, "Kasun");
        await Month(teacher.Client, classId);

        var first = await Pay(teacher.Client, student, 700m);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var receipt = await Json(first);
        Assert.StartsWith("R-", receipt.GetProperty("receiptNo").GetString());
        Assert.Equal("Cash", receipt.GetProperty("method").GetString());
        Assert.Equal("PartlyPaid", Row(await Month(teacher.Client, classId), student).GetProperty("status").GetString());

        var second = await Json(await Pay(teacher.Client, student, 1300m, method: "BankTransfer"));
        Assert.NotEqual(receipt.GetProperty("receiptNo").GetString(), second.GetProperty("receiptNo").GetString());
        var month = await Month(teacher.Client, classId);
        Assert.Equal("Paid", Row(month, student).GetProperty("status").GetString());
        Assert.Equal(2000m, month.GetProperty("totals").GetProperty("collected").GetDecimal());
        Assert.Equal(0m, month.GetProperty("totals").GetProperty("outstanding").GetDecimal());

        Assert.Equal("nothing_due", await Code(await Pay(teacher.Client, student, 100m)));
    }

    [Fact]
    public async Task One_payment_is_spread_over_the_oldest_unpaid_months_first()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client, 1000m);
        var student = await AddStudent(teacher.Client, classId, "Advance");

        // Paying for this month and the next two in one go: the two future months are billed as they are paid.
        var months = new[] { ThisMonth, MonthsAhead(1), MonthsAhead(2) };
        var paid = await Pay(teacher.Client, student, 2500m, months);
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        var allocations = (await Json(paid)).GetProperty("allocations").EnumerateArray()
            .Select(a => (a.GetProperty("month").GetString(), a.GetProperty("amount").GetDecimal())).ToList();
        Assert.Equal([(months[0], 1000m), (months[1], 1000m), (months[2], 500m)], allocations);

        var detail = await Json(await teacher.Client.GetAsync($"/api/fees/enrolments/{student}"));
        var thisMonth = detail.GetProperty("dues").EnumerateArray().Single();
        Assert.Equal("Paid", thisMonth.GetProperty("status").GetString());

        Assert.Equal("amount_exceeds_outstanding", await Code(await Pay(teacher.Client, student, 5000m, months)));
        Assert.Equal("month_invalid", await Code(await Pay(teacher.Client, student, 100m, [MonthsAhead(30)])));
        Assert.Equal("month_invalid", await Code(await Pay(teacher.Client, student, 100m, ["not-a-month"])));
    }

    [Fact]
    public async Task A_payment_must_have_a_sensible_amount_method_and_date()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client);
        var student = await AddStudent(teacher.Client, classId, "Rules");

        Assert.Equal("amount_invalid", await Code(await Pay(teacher.Client, student, 0m)));
        Assert.Equal("amount_invalid", await Code(await Pay(teacher.Client, student, -5m)));
        Assert.Equal("amount_invalid", await Code(await Pay(teacher.Client, student, 10.555m)));
        Assert.Equal("method_invalid", await Code(await Pay(teacher.Client, student, 100m, method: "Barter")));
        Assert.Equal("paid_on_in_future", await Code(await Pay(teacher.Client, student, 100m, paidOn: Today.AddDays(1))));
        Assert.Equal("enrolment_not_found", await Code(await Pay(teacher.Client, Guid.NewGuid(), 100m)));
        Assert.Equal(HttpStatusCode.Created, (await Pay(teacher.Client, student, 100m, method: "Online", paidOn: Today.AddDays(-2))).StatusCode);
    }

    [Fact]
    public async Task A_free_card_student_owes_nothing_and_cannot_be_paid_for()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client);
        var free = await AddStudent(teacher.Client, classId, "Free", "FreeCard");
        await Month(teacher.Client, classId);

        Assert.Equal("nothing_due", await Code(await Pay(teacher.Client, free, 100m)));
        var detail = await Json(await teacher.Client.GetAsync($"/api/fees/enrolments/{free}"));
        Assert.Equal(0m, detail.GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task Voiding_keeps_the_payment_on_record_with_a_reason_and_gives_the_month_back()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client, 2000m);
        var student = await AddStudent(teacher.Client, classId, "Void");
        await Month(teacher.Client, classId);
        var payment = await Json(await Pay(teacher.Client, student, 2000m));
        var id = payment.GetProperty("id").GetGuid();

        Assert.Equal("reason_required", await Code(await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{id}/void", new { reason = "  " })));
        Assert.Equal("reason_too_long", await Code(await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{id}/void", new { reason = new string('x', 201) })));

        var voided = await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{id}/void", new { reason = "Entered twice" });
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        var body = await Json(voided);
        Assert.Equal("Entered twice", body.GetProperty("voidReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("voidedAt").ValueKind);

        Assert.Equal("Unpaid", Row(await Month(teacher.Client, classId), student).GetProperty("status").GetString());
        var detail = await Json(await teacher.Client.GetAsync($"/api/fees/enrolments/{student}"));
        var listed = Assert.Single(detail.GetProperty("payments").EnumerateArray());
        Assert.Equal(id, listed.GetProperty("id").GetGuid());
        Assert.Equal(2000m, detail.GetProperty("outstanding").GetDecimal());

        Assert.Equal("payment_voided", await Code(await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{id}/void", new { reason = "Again" })));

        // The month can be paid again, and the new receipt gets the next number.
        var again = await Json(await Pay(teacher.Client, student, 2000m));
        Assert.NotEqual(payment.GetProperty("receiptNo").GetString(), again.GetProperty("receiptNo").GetString());
        Assert.Equal("Paid", Row(await Month(teacher.Client, classId), student).GetProperty("status").GetString());
    }

    [Fact]
    public async Task One_teachers_fees_are_closed_to_another_teacher()
    {
        var owner = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync("Sunil");
        var classId = await ClassWithFee(owner.Client);
        var student = await AddStudent(owner.Client, classId, "Private");
        await Month(owner.Client, classId);
        var payment = await Json(await Pay(owner.Client, student, 500m));

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/fees/classes/{classId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/fees/enrolments/{student}")).StatusCode);
        Assert.Equal("enrolment_not_found", await Code(await Pay(other.Client, student, 100m)));
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/fees/payments/{payment.GetProperty("id").GetGuid()}/void", new { reason = "Mine now" })).StatusCode);
    }

    [Fact]
    public async Task Every_payment_and_void_is_logged()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client);
        var student = await AddStudent(teacher.Client, classId, "Audit");
        await Month(teacher.Client, classId);
        var payment = await Json(await Pay(teacher.Client, student, 300m));
        var id = payment.GetProperty("id").GetGuid();
        await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{id}/void", new { reason = "Wrong student" });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeesDbContext>();
        var entries = await db.Set<AuditLogEntry>().Where(e => e.EntityId == id).ToListAsync();
        Assert.Contains(entries, e => e.Action == AuditAction.Created);
        var update = Assert.Single(entries, e => e.Action == AuditAction.Updated);
        Assert.Contains("Wrong student", update.After);
        Assert.NotNull(update.UserId);
    }

    [Fact]
    public async Task A_student_sees_their_own_months_and_receipts_but_not_voided_ones()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await ClassWithFee(teacher.Client, 1500m);
        var phone = NewPhone();
        var created = await teacher.Client.PostAsJsonAsync("/api/students", new { phone, firstName = "Dilani", lastName = "Silva", classId, feePlan = "Full" });
        var enrolment = (await Json(created)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
        var otp = (await Json(created)).GetProperty("oneTimePassword").GetString()!;
        await Month(teacher.Client, classId);
        await Pay(teacher.Client, enrolment, 1000m);
        var wrong = await Json(await Pay(teacher.Client, enrolment, 200m));
        await teacher.Client.PostAsJsonAsync($"/api/fees/payments/{wrong.GetProperty("id").GetGuid()}/void", new { reason = "Typo" });

        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = otp, newPassword = "Student-pass-1", username = "s" + Guid.NewGuid().ToString("N")[..10] });
        var student = factory.CreateClient();
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);

        var mine = Assert.Single((await Json(await student.GetAsync("/api/fees/mine"))).EnumerateArray());
        Assert.Equal(classId, mine.GetProperty("classId").GetGuid());
        Assert.Equal(500m, mine.GetProperty("outstanding").GetDecimal());
        var thisMonth = Assert.Single(mine.GetProperty("months").EnumerateArray());
        Assert.Equal("PartlyPaid", thisMonth.GetProperty("status").GetString());
        var receipt = Assert.Single(mine.GetProperty("receipts").EnumerateArray());
        Assert.Equal(1000m, receipt.GetProperty("amount").GetDecimal());

        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/fees/classes/{classId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync("/api/fees/mine")).StatusCode);
    }
}
