using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GuruBook.Modules.Fees;
using GuruBook.Modules.Fees.Domain;
using GuruBook.Modules.Fees.Services;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuruBook.Api.Tests;

/// <summary>
/// Automatic fee reminders: the day before a period starts, on the due date and weekly after it; each step once; never in quiet hours;
/// never for a paid fee; and the teacher's say per class and per student.
/// </summary>
public class FeeRemindersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phoneCounter = 9500;

    // A fee for December 2030, due the 10th, so every date in these tests is far from anything another test makes.
    private const string Month = "2030-12";
    private static readonly DateOnly DueDate = new(2030, 12, 10);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static FixedClock At(int year, int month, int day, int hour = 12) => new(new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.FromMinutes(330)));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<Guid> NewClass(HttpClient teacher, string title = "Reminder class")
    {
        var venue = await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Home class", district = "Kandy" });
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId = (await Json(venue)).GetProperty("id").GetGuid(), monthlyFee = 2500m,
            slots = new[] { new { day = 1, start = "16:00", end = "18:00" } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private sealed record Student(Guid Enrolment, HttpClient Client);

    /// <summary>A student with a sign-in, whose fee is the one for December 2030, due the 10th.</summary>
    private async Task<Student> NewStudent(HttpClient teacher, Guid classId, string first = "Amara")
    {
        var phone = $"075{Interlocked.Increment(ref _phoneCounter):D7}";
        var response = await teacher.PostAsJsonAsync("/api/students", new { phone, firstName = first, lastName = "Silva", classId, feePlan = "Full" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var enrolment = body.GetProperty("row").GetProperty("enrolmentId").GetGuid();
        var otp = body.GetProperty("oneTimePassword").GetString()!;

        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        var setup = factory.CreateClient();
        setup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password", new { currentPassword = otp, newPassword = "Student-pass-1", username = "r" + Guid.NewGuid().ToString("N")[..10] });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(changed)).GetProperty("accessToken").GetString()!);

        // Make the student's fee and point it at December 2030.
        await teacher.GetAsync($"/api/fees/enrolments/{enrolment}");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FeesDbContext>();
        var due = await db.Dues.IgnoreQueryFilters().Where(d => d.EnrolmentId == enrolment).OrderBy(d => d.Month).FirstAsync();
        due.Month = Month;
        due.LastMonth = Month;
        due.DueDate = DueDate;
        await db.SaveChangesAsync();
        return new Student(enrolment, client);
    }

    private async Task Remind(TimeProvider clock)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await new FeeReminderService(
            sp.GetRequiredService<FeesDbContext>(), sp.GetRequiredService<IClassRoster>(), sp.GetRequiredService<IClassDirectory>(), sp.GetRequiredService<INotifier>(),
            sp.GetRequiredService<IMessageRouter>(), sp.GetRequiredService<IStudentContacts>(), sp.GetRequiredService<ITeacherDirectory>(), clock)
            .RunAsync(CancellationToken.None);
    }

    private static async Task<JsonElement[]> Heard(HttpClient client, string kind) =>
        [.. (await Json(await client.GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray().Where(n => n.GetProperty("kind").GetString() == kind)];

    private async Task<List<FeeReminder>> Log(Guid enrolment)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FeesDbContext>().Reminders.IgnoreQueryFilters().Where(r => r.EnrolmentId == enrolment).ToListAsync();
    }

    [Fact]
    public async Task The_student_is_told_the_day_before_the_period_starts_once()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));

        await Remind(At(2030, 11, 29));
        Assert.Empty(await Heard(student.Client, "fee_before"));

        await Remind(At(2030, 11, 30));
        await Remind(At(2030, 11, 30, 15));
        var told = Assert.Single(await Heard(student.Client, "fee_before"));
        var p = told.GetProperty("params");
        Assert.Equal(2500m, p.GetProperty("amount").GetDecimal());
        Assert.Equal(Month, p.GetProperty("month").GetString());
        Assert.Equal("2030-12-10", p.GetProperty("dueDate").GetString());
        Assert.Equal("Reminder class", p.GetProperty("classTitle").GetString());
        Assert.Equal("/welcome", told.GetProperty("link").GetString());
        Assert.Equal("before", Assert.Single(await Log(student.Enrolment)).Step);
    }

    [Fact]
    public async Task The_student_is_told_on_the_due_date_and_not_before()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));

        await Remind(At(2030, 12, 5));
        await Remind(At(2030, 12, 9));
        Assert.Empty(await Heard(student.Client, "fee_due_today"));

        await Remind(At(2030, 12, 10));
        await Remind(At(2030, 12, 10, 18));
        Assert.Single(await Heard(student.Client, "fee_due_today"));
        Assert.Contains(await Log(student.Enrolment), r => r.Step == "due" && r.Number == 0);
    }

    [Fact]
    public async Task A_missed_day_is_caught_up_within_the_week_but_only_once()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));

        await Remind(At(2030, 12, 13));
        await Remind(At(2030, 12, 14));
        Assert.Single(await Heard(student.Client, "fee_due_today"));
    }

    [Fact]
    public async Task After_the_due_date_the_student_is_reminded_every_week_while_unpaid()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));
        await Remind(At(2030, 12, 10));

        await Remind(At(2030, 12, 16));
        Assert.Empty(await Heard(student.Client, "fee_overdue"));

        await Remind(At(2030, 12, 17));
        await Remind(At(2030, 12, 19));
        var first = Assert.Single(await Heard(student.Client, "fee_overdue"));
        Assert.Equal(7, first.GetProperty("params").GetProperty("daysLate").GetInt32());

        await Remind(At(2030, 12, 24));
        Assert.Equal(2, (await Heard(student.Client, "fee_overdue")).Length);
        Assert.Equal([1, 2], (await Log(student.Enrolment)).Where(r => r.Step == "late").Select(r => r.Number).Order());
    }

    [Fact]
    public async Task A_long_gap_sends_only_the_current_week_not_a_pile()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));

        await Remind(At(2031, 1, 14));

        var late = Assert.Single(await Heard(student.Client, "fee_overdue"));
        Assert.Equal(35, late.GetProperty("params").GetProperty("daysLate").GetInt32());
        Assert.Equal(5, late.GetProperty("params").GetProperty("weeks").GetInt32());
    }

    [Fact]
    public async Task Reminders_stop_once_the_fee_is_paid()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));
        await Remind(At(2030, 12, 10));

        // The fee is paid in full (a payment spread over it), then a week passes. The month is years ahead, so the payment is written straight to the books.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FeesDbContext>();
            var due = await db.Dues.IgnoreQueryFilters().SingleAsync(d => d.EnrolmentId == student.Enrolment);
            var payment = new Payment { TenantId = due.TenantId, EnrolmentId = student.Enrolment, ReceiptNo = "R-T" + Guid.NewGuid().ToString("N")[..8], Amount = due.AmountDue, Method = PaymentMethod.Cash, PaidOn = DueDate };
            db.Payments.Add(payment);
            db.Allocations.Add(new PaymentAllocation { TenantId = due.TenantId, PaymentId = payment.Id, FeeDueId = due.Id, Amount = due.AmountDue });
            await db.SaveChangesAsync();
        }

        await Remind(At(2030, 12, 17));

        Assert.Empty(await Heard(student.Client, "fee_overdue"));
    }

    [Fact]
    public async Task Nothing_is_sent_between_eight_in_the_evening_and_seven_in_the_morning()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));

        await Remind(At(2030, 12, 10, 6));
        await Remind(At(2030, 12, 10, 20));
        await Remind(At(2030, 12, 10, 23));
        Assert.Empty(await Heard(student.Client, "fee_due_today"));

        await Remind(At(2030, 12, 10, 7));
        Assert.Single(await Heard(student.Client, "fee_due_today"));
        Assert.True(FeeReminderService.IsQuietHour(19 + 1) && FeeReminderService.IsQuietHour(0) && !FeeReminderService.IsQuietHour(7) && !FeeReminderService.IsQuietHour(19));
    }

    [Fact]
    public async Task A_class_with_reminders_turned_off_sends_none()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var student = await NewStudent(teacher.Client, classId);

        var off = await Json(await teacher.Client.PutAsJsonAsync($"/api/fees/classes/{classId}/reminders", new { enabled = false, weeklyLimit = (int?)null }));
        Assert.False(off.GetProperty("enabled").GetBoolean());
        await Remind(At(2030, 12, 10));
        Assert.Empty(await Heard(student.Client, "fee_due_today"));

        await teacher.Client.PutAsJsonAsync($"/api/fees/classes/{classId}/reminders", new { enabled = true, weeklyLimit = (int?)null });
        await Remind(At(2030, 12, 10));
        Assert.Single(await Heard(student.Client, "fee_due_today"));
    }

    [Fact]
    public async Task The_teacher_can_limit_how_many_weekly_reminders_follow_the_due_date()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var student = await NewStudent(teacher.Client, classId);
        await teacher.Client.PutAsJsonAsync($"/api/fees/classes/{classId}/reminders", new { enabled = true, weeklyLimit = 2 });

        foreach (var day in new[] { 17, 24, 31 })
        {
            await Remind(day == 31 ? At(2030, 12, 31) : At(2030, 12, day));
        }

        Assert.Equal(2, (await Heard(student.Client, "fee_overdue")).Length);
    }

    [Fact]
    public async Task A_skipped_student_gets_no_reminders_until_the_skip_is_removed()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var student = await NewStudent(teacher.Client, classId);
        var other = await NewStudent(teacher.Client, classId, "Bimal");

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.PutAsJsonAsync($"/api/fees/enrolments/{student.Enrolment}/reminders", new { skip = true })).StatusCode);
        var settings = await Json(await teacher.Client.GetAsync($"/api/fees/classes/{classId}/reminders"));
        Assert.Equal([student.Enrolment], settings.GetProperty("skippedEnrolments").EnumerateArray().Select(e => e.GetGuid()));

        await Remind(At(2030, 12, 10));
        Assert.Empty(await Heard(student.Client, "fee_due_today"));
        Assert.Single(await Heard(other.Client, "fee_due_today"));
        Assert.True((await Json(await teacher.Client.GetAsync($"/api/fees/enrolments/{student.Enrolment}"))).GetProperty("remindersSkipped").GetBoolean());

        await teacher.Client.PutAsJsonAsync($"/api/fees/enrolments/{student.Enrolment}/reminders", new { skip = false });
        await Remind(At(2030, 12, 10));
        Assert.Single(await Heard(student.Client, "fee_due_today"));
    }

    [Fact]
    public async Task A_student_who_has_left_the_class_is_not_reminded()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var student = await NewStudent(teacher.Client, classId);
        var left = await teacher.Client.PutAsJsonAsync($"/api/students/enrolments/{student.Enrolment}", new { status = "Left" });
        Assert.True(left.IsSuccessStatusCode, await left.Content.ReadAsStringAsync());

        await Remind(At(2030, 12, 10));

        Assert.Empty(await Heard(student.Client, "fee_due_today"));
    }

    [Fact]
    public async Task The_teacher_sees_what_was_sent_to_a_student()
    {
        var teacher = await factory.NewTeacherAsync();
        var student = await NewStudent(teacher.Client, await NewClass(teacher.Client));
        await Remind(At(2030, 11, 30));
        await Remind(At(2030, 12, 10));
        await Remind(At(2030, 12, 17));

        var fees = await Json(await teacher.Client.GetAsync($"/api/fees/enrolments/{student.Enrolment}"));
        var log = fees.GetProperty("reminders").EnumerateArray().Select(r => (r.GetProperty("step").GetString(), r.GetProperty("number").GetInt32(), r.GetProperty("channel").GetString())).ToArray();
        Assert.Equal(3, log.Length);
        Assert.Contains(("before", 0, "App"), log);
        Assert.Contains(("due", 0, "App"), log);
        Assert.Contains(("late", 1, "App"), log);
        Assert.False(fees.GetProperty("remindersSkipped").GetBoolean());
    }

    [Fact]
    public async Task Reminder_settings_check_the_weekly_limit_and_belong_to_one_teacher()
    {
        var teacher = await factory.NewTeacherAsync();
        var other = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var student = await NewStudent(teacher.Client, classId);

        foreach (var bad in new int?[] { 0, 9, -1 })
        {
            var response = await teacher.Client.PutAsJsonAsync($"/api/fees/classes/{classId}/reminders", new { enabled = true, weeklyLimit = bad });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("weekly_limit_invalid", (await Json(response)).GetProperty("code").GetString());
        }

        var defaults = await Json(await teacher.Client.GetAsync($"/api/fees/classes/{classId}/reminders"));
        Assert.True(defaults.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("weeklyLimit").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/fees/classes/{classId}/reminders")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsJsonAsync($"/api/fees/classes/{classId}/reminders", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsJsonAsync($"/api/fees/enrolments/{student.Enrolment}/reminders", new { skip = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/fees/classes/{classId}/reminders")).StatusCode);
    }

    [Fact]
    public async Task On_the_last_day_of_the_month_next_months_fee_is_made_so_the_reminder_has_something_to_point_at()
    {
        var teacher = await factory.NewTeacherAsync();
        var classId = await NewClass(teacher.Client);
        var phone = $"075{Interlocked.Increment(ref _phoneCounter):D7}";
        var added = await teacher.Client.PostAsJsonAsync("/api/students", new { phone, firstName = "Last", lastName = "Day", classId, feePlan = "Full" });
        var enrolment = (await Json(added)).GetProperty("row").GetProperty("enrolmentId").GetGuid();

        var colombo = DateTime.UtcNow.AddMinutes(330);
        var lastDay = new DateOnly(colombo.Year, colombo.Month, DateTime.DaysInMonth(colombo.Year, colombo.Month));
        var clock = new FixedClock(new DateTimeOffset(lastDay.Year, lastDay.Month, lastDay.Day, 12, 0, 0, TimeSpan.FromMinutes(330)));
        var job = new FeeDueJob(factory.Services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), clock, NullLogger<FeeDueJob>.Instance);

        await job.RunOnceAsync(CancellationToken.None);

        using var scope = factory.Services.CreateScope();
        var months = await scope.ServiceProvider.GetRequiredService<FeesDbContext>().Dues.IgnoreQueryFilters().Where(d => d.EnrolmentId == enrolment).Select(d => d.Month).ToListAsync();
        Assert.Contains(lastDay.AddDays(1).ToString("yyyy-MM"), months);
        Assert.Contains(lastDay.ToString("yyyy-MM"), months);
    }

    [Fact]
    public void The_step_for_a_fee_follows_the_calendar()
    {
        var due = new FeeDue { Month = "2030-12", LastMonth = "2030-12", DueDate = new DateOnly(2030, 12, 10) };
        Assert.Null(FeeReminderService.StepFor(due, new DateOnly(2030, 11, 29), null));
        Assert.Equal(("before", 0), FeeReminderService.StepFor(due, new DateOnly(2030, 11, 30), null));
        Assert.Null(FeeReminderService.StepFor(due, new DateOnly(2030, 12, 5), null));
        Assert.Equal(("due", 0), FeeReminderService.StepFor(due, new DateOnly(2030, 12, 10), null));
        Assert.Equal(("due", 0), FeeReminderService.StepFor(due, new DateOnly(2030, 12, 16), null));
        Assert.Equal(("late", 1), FeeReminderService.StepFor(due, new DateOnly(2030, 12, 17), null));
        Assert.Equal(("late", 3), FeeReminderService.StepFor(due, new DateOnly(2030, 12, 31), null));
        Assert.Null(FeeReminderService.StepFor(due, new DateOnly(2030, 12, 31), 2));
        Assert.Null(FeeReminderService.StepFor(due, due.DueDate.AddDays(7 * 27), null));

        // Due on the first of the month: the day before is both "before the period" and before the due date.
        var first = new FeeDue { Month = "2030-12", LastMonth = "2030-12", DueDate = new DateOnly(2030, 12, 1) };
        Assert.Equal(("before", 0), FeeReminderService.StepFor(first, new DateOnly(2030, 11, 30), null));
        Assert.Equal(("due", 0), FeeReminderService.StepFor(first, new DateOnly(2030, 12, 1), null));
    }
}
