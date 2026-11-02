using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GuruBook.Modules.Fees;
using GuruBook.Modules.Fees.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Api.Tests;

/// <summary>Fee cycles: a class can be paid monthly or every 3, 6 or 12 months, each fee has a due date, and late fees are marked.</summary>
public class FeeCyclesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 9000;

    private static string NewPhone() => $"076{Interlocked.Increment(ref _phoneCounter):D7}";

    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    private static string ThisMonth => Today.ToString("yyyy-MM");

    private static string Ahead(int months) => Today.AddMonths(months).ToString("yyyy-MM");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private object ClassBody(Guid venueId, decimal fee, int? cycle, int? payBy, string title = "Cycle class") => new
    {
        title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English", venueId, monthlyFee = fee,
        slots = new[] { new { day = 1, start = "16:00", end = "18:00" } }, feeCycleMonths = cycle, payByDay = payBy,
    };

    private async Task<Guid> NewVenue(HttpClient teacher)
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home class", district = "Kandy" });
        return (await Json(venue)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> NewClass(HttpClient teacher, decimal fee = 3000m, int? cycle = null, int? payBy = null)
    {
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", ClassBody(await NewVenue(teacher), fee, cycle, payBy));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddStudent(HttpClient teacher, Guid classId, string first = "Kasun")
    {
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone = NewPhone(), firstName = first, lastName = "Perera", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("row").GetProperty("enrolmentId").GetGuid();
    }

    private static async Task<JsonElement> Month(HttpClient teacher, Guid classId, string? month = null) =>
        await Json(await teacher.GetAsync($"/api/fees/classes/{classId}" + (month is null ? "" : $"?month={month}")));

    private static JsonElement Row(JsonElement month, Guid enrolment) =>
        month.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("enrolmentId").GetGuid() == enrolment);

    private async Task Edit(Action<FeesDbContext> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeesDbContext>();
        change(db);
        await db.SaveChangesAsync();
    }

    private async Task<List<GuruBook.Modules.Fees.Domain.FeeDue>> DuesOf(Guid enrolment)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeesDbContext>();
        return (await db.Dues.IgnoreQueryFilters().Where(d => d.EnrolmentId == enrolment).ToListAsync()).OrderBy(d => d.Month).ToList();
    }

    private async Task Ensure(Guid classId, params string[] months)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FeeDueService>().EnsureForAllTeachersAsync(months, CancellationToken.None);
    }

    [Fact]
    public async Task A_class_is_monthly_and_due_on_the_10th_unless_the_teacher_says_otherwise_and_bad_values_are_refused()
    {
        var teacher = await factory.NewTeacherAsync();
        var venue = await NewVenue(teacher.Client);

        var plain = await Json(await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(venue, 2500m, null, null)));
        Assert.Equal(1, plain.GetProperty("feeCycleMonths").GetInt32());
        Assert.Equal(10, plain.GetProperty("payByDay").GetInt32());

        var quarterly = await Json(await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(venue, 7000m, 3, 7, "Quarterly")));
        Assert.Equal(3, quarterly.GetProperty("feeCycleMonths").GetInt32());
        Assert.Equal(7, quarterly.GetProperty("payByDay").GetInt32());

        // Leaving the fields out when editing keeps what the class has.
        var id = quarterly.GetProperty("id").GetGuid();
        var kept = await Json(await teacher.Client.PutAsJsonAsync($"/api/classes/classes/{id}", ClassBody(venue, 7500m, null, null, "Quarterly")));
        Assert.Equal(3, kept.GetProperty("feeCycleMonths").GetInt32());
        Assert.Equal(7, kept.GetProperty("payByDay").GetInt32());

        Assert.Equal("fee_cycle_invalid", await Code(await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(venue, 2500m, 2, 10))));
        Assert.Equal("pay_by_day_invalid", await Code(await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(venue, 2500m, 1, 29))));
        Assert.Equal("pay_by_day_invalid", await Code(await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(venue, 2500m, 1, 0))));
    }

    [Fact]
    public async Task A_monthly_fee_is_due_on_the_pay_by_day_and_a_new_student_gets_a_week_after_joining()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, 2000m, 1, 1);
        var student = await AddStudent(teacher.Client, classId);

        var row = Row(await Month(teacher.Client, classId), student);
        var expected = new[] { new DateOnly(Today.Year, Today.Month, 1), Today.AddDays(7) }.Max();
        Assert.Equal(expected.ToString("yyyy-MM-dd"), row.GetProperty("dueDate").GetString());
        Assert.Equal(1, row.GetProperty("periodMonths").GetInt32());
        Assert.Equal(ThisMonth, row.GetProperty("month").GetString());
    }

    [Fact]
    public async Task A_three_monthly_class_makes_one_fee_for_the_whole_period_and_the_next_one_when_it_begins()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, 7000m, 3, 5);
        var student = await AddStudent(teacher.Client, classId);

        var periodStart = FeeMonths.PeriodStart(ThisMonth, 3);
        var periodEnd = FeeMonths.Add(periodStart, 2);
        await Ensure(classId, [.. FeeMonths.Range(ThisMonth, periodEnd)]);
        var dues = await DuesOf(student);
        var first = Assert.Single(dues);
        Assert.Equal(ThisMonth, first.Month);
        Assert.Equal(periodEnd, first.LastMonth);
        Assert.Equal(7000m, first.AmountDue);

        // Every month of the period shows the same fee, with its period.
        var lastMonthOfPeriod = await Month(teacher.Client, classId, ThisMonth);
        Assert.Equal(7000m, Row(lastMonthOfPeriod, student).GetProperty("due").GetDecimal());

        await Ensure(classId, FeeMonths.Add(periodEnd, 1));
        var next = (await DuesOf(student)).Last();
        Assert.Equal(FeeMonths.Add(periodEnd, 1), next.Month);
        Assert.Equal(3, next.PeriodMonths);
        Assert.Equal(new DateOnly(int.Parse(next.Month[..4]), int.Parse(next.Month[5..]), 5), next.DueDate);
        Assert.Equal(2, (await DuesOf(student)).Count);
    }

    [Fact]
    public async Task A_payment_toward_any_month_of_the_period_goes_to_the_period_fee()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, 6000m, 6, 10);
        var student = await AddStudent(teacher.Client, classId);
        var periodEnd = FeeMonths.Add(FeeMonths.PeriodStart(ThisMonth, 6), 5);

        var paid = await teacher.Client.PostAsJsonAsync("/api/fees/payments",
            new { enrolmentId = student, amount = 6000m, method = "Cash", months = new[] { periodEnd } });
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);

        var row = Row(await Month(teacher.Client, classId, ThisMonth), student);
        Assert.Equal("Paid", row.GetProperty("status").GetString());
        Assert.Single(await DuesOf(student));
    }

    [Fact]
    public async Task A_fee_is_marked_overdue_due_today_or_due_soon_until_it_is_paid()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, 2000m);
        var late = await AddStudent(teacher.Client, classId, "Late");
        var today = await AddStudent(teacher.Client, classId, "Today");
        var soon = await AddStudent(teacher.Client, classId, "Soon");
        var far = await AddStudent(teacher.Client, classId, "Far");
        var paid = await AddStudent(teacher.Client, classId, "Paid");
        await Month(teacher.Client, classId);

        // Move the fees to last month: open fees get their date refreshed from the class's pay-by day, closed ones keep what was set.
        var last = FeeMonths.Add(ThisMonth, -1);
        await Edit(db =>
        {
            foreach (var due in db.Dues.IgnoreQueryFilters().Where(d => d.ClassId == classId))
            {
                due.Month = last;
                due.LastMonth = last;
                due.DueDate = due.EnrolmentId == late ? Today.AddDays(-3)
                    : due.EnrolmentId == today ? Today
                    : due.EnrolmentId == soon ? Today.AddDays(2)
                    : Today.AddDays(10);
            }
        });
        Assert.Equal(HttpStatusCode.Created, (await teacher.Client.PostAsJsonAsync("/api/fees/payments", new { enrolmentId = paid, amount = 2000m, method = "Cash" })).StatusCode);
        await Edit(db =>
        {
            foreach (var due in db.Dues.IgnoreQueryFilters().Where(d => d.EnrolmentId == paid))
            {
                due.DueDate = Today.AddDays(-20);
            }
        });

        var month = await Month(teacher.Client, classId, last);
        Assert.Equal("Overdue", Row(month, late).GetProperty("timing").GetString());
        Assert.Equal(3, Row(month, late).GetProperty("daysLate").GetInt32());
        Assert.Equal("DueToday", Row(month, today).GetProperty("timing").GetString());
        Assert.Equal("DueSoon", Row(month, soon).GetProperty("timing").GetString());
        Assert.Equal("", Row(month, far).GetProperty("timing").GetString());
        Assert.Equal("", Row(month, paid).GetProperty("timing").GetString());
        Assert.Equal(1, month.GetProperty("totals").GetProperty("overdue").GetInt32());

        var overview = await Json(await teacher.Client.GetAsync($"/api/fees/overview?month={last}"));
        var mine = overview.GetProperty("classes").EnumerateArray().Single(c => c.GetProperty("classId").GetGuid() == classId);
        Assert.Equal(1, mine.GetProperty("totals").GetProperty("overdue").GetInt32());
    }

    [Fact]
    public async Task Changing_a_class_to_a_longer_cycle_starts_with_the_next_fee_and_never_overlaps_what_was_billed()
    {
        var teacher = await factory.NewTeacherAsync();
        var venue = await NewVenue(teacher.Client);
        var created = await Json(await teacher.Client.PostAsJsonAsync("/api/classes/classes", ClassBody(venue, 1000m, 1, 10, "Switch")));
        var classId = created.GetProperty("id").GetGuid();
        var student = await AddStudent(teacher.Client, classId);
        await Ensure(classId, ThisMonth);

        var edit = await teacher.Client.PutAsJsonAsync($"/api/classes/classes/{classId}", ClassBody(venue, 2800m, 3, 10, "Switch"));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var periodEnd = FeeMonths.Add(FeeMonths.PeriodStart(ThisMonth, 3), 2);
        await Ensure(classId, [.. FeeMonths.Range(ThisMonth, FeeMonths.Add(periodEnd, 3))]);

        var dues = await DuesOf(student);
        Assert.True(dues.Count >= 2);
        for (var i = 1; i < dues.Count; i++)
        {
            Assert.True(string.CompareOrdinal(dues[i].Month, dues[i - 1].LastMonth) > 0, $"{dues[i - 1].Month}-{dues[i - 1].LastMonth} overlaps {dues[i].Month}");
        }

        // Every month asked for is covered by exactly one fee.
        foreach (var month in FeeMonths.Range(ThisMonth, FeeMonths.Add(periodEnd, 3)))
        {
            Assert.Single(dues, d => d.Covers(month));
        }
    }

    [Fact]
    public async Task A_student_sees_the_due_date_and_whether_a_fee_is_late()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client, 1500m);
        var phone = NewPhone();
        var created = await Json(await teacher.Client.PostAsJsonAsync("/api/students", new { phone, firstName = "Dilani", lastName = "Silva", classId, feePlan = "Full" }));
        var enrolment = created.GetProperty("row").GetProperty("enrolmentId").GetGuid();
        var otp = created.GetProperty("oneTimePassword").GetString()!;
        await Month(teacher.Client, classId);
        await Edit(db =>
        {
            foreach (var due in db.Dues.IgnoreQueryFilters().Where(d => d.EnrolmentId == enrolment))
            {
                due.DueDate = Today.AddDays(-2);
            }
        });

        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = otp, newPassword = "Student-pass-1", username = "s" + Guid.NewGuid().ToString("N")[..10] });
        var student = factory.CreateClient();
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);

        var mine = Assert.Single((await Json(await student.GetAsync("/api/fees/mine"))).EnumerateArray());
        var due = Assert.Single(mine.GetProperty("months").EnumerateArray());
        Assert.Equal("Overdue", due.GetProperty("timing").GetString());
        Assert.Equal(2, due.GetProperty("daysLate").GetInt32());
        Assert.Equal(Today.AddDays(-2).ToString("yyyy-MM-dd"), due.GetProperty("dueDate").GetString());
    }
}
