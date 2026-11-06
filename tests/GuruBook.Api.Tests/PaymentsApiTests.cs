using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GuruBook.Modules.Payments;
using GuruBook.Modules.Payments.Domain;
using GuruBook.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GuruBook.Api.Tests;

/// <summary>
/// Paying a fee by bank transfer or LankaQR (Pro): the teacher's details, what a payer is shown, "I've paid" with a slip, and the teacher
/// confirming or rejecting it. GuruBook never sees the money, so every one of these is about what was said and who said it.
/// </summary>
public class PaymentsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _phones = 6000;

    private static string NewPhone() => $"076{Interlocked.Increment(ref _phones):D7}";

    /// <summary>The smallest valid JPEG header; the API only looks at the first bytes.</summary>
    private static byte[] Jpeg(string tail = "slip") => [0xFF, 0xD8, 0xFF, 0xE0, .. Encoding.UTF8.GetBytes(tail)];

    private static string Month() => DateTime.UtcNow.AddMinutes(330).ToString("yyyy-MM");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Code(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? null : (await Json(response)).GetProperty("code").GetString();

    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

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

    private async Task<Guid> ClassOf(HttpClient teacher, string title, decimal fee = 2000m)
    {
        var venueId = (await Json(await teacher.PostAsJsonAsync("/api/classes/venues", new { name = "Hall " + title, district = "Kandy" }))).GetProperty("id").GetGuid();
        var created = await teacher.PostAsJsonAsync("/api/classes/classes", new
        {
            title, subjectId = factory.MathsId, streamId = factory.StreamId, examYear = 2027, medium = "English",
            venueId, monthlyFee = fee, slots = Enumerable.Range(1, 7).Select(day => new { day, start = "16:00", end = "18:00" }).ToArray(),
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await Json(created)).GetProperty("id").GetGuid();
    }

    private sealed record Learner(Guid StudentId, Guid EnrolmentId, string Phone, string OneTimePassword);

    private static async Task<Learner> Enrol(HttpClient teacher, Guid classId, string first, string? guardianPhone = null)
    {
        var phone = NewPhone();
        var response = await teacher.PostAsJsonAsync(
            "/api/students", new { phone, firstName = first, lastName = "Silva", classId, feePlan = "Full", guardianPhone, guardianName = "Mrs Silva" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        var row = body.GetProperty("row");
        return new Learner(
            row.GetProperty("studentId").GetGuid(), row.GetProperty("enrolmentId").GetGuid(), phone,
            body.GetProperty("oneTimePassword").GetString()!);
    }

    private HttpClient Bearer(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Signs in with the one-time password and picks a username, which is what every student and parent does on their first visit.</summary>
    private async Task<HttpClient> SignIn(string phone, string otp, string password)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/api/identity/login", new { username = phone, password = otp });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setup = Bearer((await Json(login)).GetProperty("accessToken").GetString()!);
        var changed = await setup.PostAsJsonAsync("/api/identity/change-password",
            new { currentPassword = otp, newPassword = password, username = "u" + Guid.NewGuid().ToString("N")[..10] });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        return Bearer((await Json(changed)).GetProperty("accessToken").GetString()!);
    }

    /// <summary>Asking for the student's fees as the teacher is what makes this month's due, exactly as opening the screen does.</summary>
    private static async Task MakeDue(HttpClient teacher, Guid enrolmentId) =>
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/api/fees/enrolments/{enrolmentId}")).StatusCode);

    private static async Task SaveDetails(HttpClient teacher) =>
        Assert.Equal(HttpStatusCode.OK, (await teacher.PutAsJsonAsync("/api/payments/details", new
        {
            bankName = "Commercial Bank", accountName = "S Perera", accountNumber = "8001234567", branch = "Kandy", note = "Write the code on the slip",
        })).StatusCode);

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string url, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return await client.PutAsync(url, content);
    }

    private static async Task<JsonElement> Claim(HttpClient payer, string url, decimal amount, string? note = null)
    {
        var response = await payer.PostAsJsonAsync(url, new { month = Month(), amount, note });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private static async Task<JsonElement> Notifications(HttpClient client, string kind)
    {
        var items = (await Json(await client.GetAsync("/api/notifications"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("kind").GetString() == kind).ToList();
        Assert.NotEmpty(items);
        return items[0];
    }

    [Fact]
    public async Task A_free_teacher_cannot_set_up_bank_payments()
    {
        var free = await factory.NewTeacherAsync("Fathima");
        Assert.Equal("plan_feature_locked", await Code(await free.Client.GetAsync("/api/payments/details")));
        Assert.Equal("plan_feature_locked", await Code(await free.Client.GetAsync("/api/payments/claims")));
        Assert.Equal("plan_feature_locked", await Code(await free.Client.PutAsJsonAsync("/api/payments/details", new { bankName = "A", accountName = "B", accountNumber = "1" })));
    }

    [Fact]
    public async Task A_pro_teacher_saves_account_details_and_a_qr()
    {
        var teacher = await ProTeacher("Pradeep");
        var empty = await Get(teacher.Client, "/api/payments/details");
        Assert.Equal("", empty.GetProperty("bankName").GetString());
        Assert.False(empty.GetProperty("hasQr").GetBoolean());

        Assert.Equal("details_incomplete", await Code(await teacher.Client.PutAsJsonAsync("/api/payments/details", new { bankName = "Commercial Bank" })));
        await SaveDetails(teacher.Client);
        var saved = await Get(teacher.Client, "/api/payments/details");
        Assert.Equal("Commercial Bank", saved.GetProperty("bankName").GetString());
        Assert.Equal("8001234567", saved.GetProperty("accountNumber").GetString());

        // Nothing but a real image is taken, and the QR is served back with its own type.
        Assert.Equal("qr_invalid", await Code(await Upload(teacher.Client, "/api/payments/qr", Encoding.UTF8.GetBytes("not an image"))));
        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.GetAsync("/api/payments/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Upload(teacher.Client, "/api/payments/qr", Jpeg("qr"))).StatusCode);
        Assert.True((await Get(teacher.Client, "/api/payments/details")).GetProperty("hasQr").GetBoolean());

        var image = await teacher.Client.GetAsync("/api/payments/qr");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NoContent, (await teacher.Client.DeleteAsync("/api/payments/qr")).StatusCode);
        Assert.False((await Get(teacher.Client, "/api/payments/details")).GetProperty("hasQr").GetBoolean());
    }

    [Fact]
    public async Task A_student_is_shown_what_to_pay_and_where_to_send_it()
    {
        var teacher = await ProTeacher("Sanath");
        var classId = await ClassOf(teacher.Client, "Pay class", fee: 3000m);
        var learner = await Enrol(teacher.Client, classId, "Kavi");
        await MakeDue(teacher.Client, learner.EnrolmentId);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");

        // The teacher has not filled their details in yet, so the screen says so rather than offering a transfer.
        var before = await Get(student, $"/api/payments/mine/{learner.EnrolmentId}?month={Month()}");
        Assert.False(before.GetProperty("canPay").GetBoolean());
        Assert.Empty((await Get(student, "/api/payments/mine")).EnumerateArray());

        await SaveDetails(teacher.Client);
        Assert.Equal(HttpStatusCode.OK, (await Upload(teacher.Client, "/api/payments/qr", Jpeg("qr"))).StatusCode);

        var way = await Get(student, $"/api/payments/mine/{learner.EnrolmentId}?month={Month()}");
        Assert.True(way.GetProperty("canPay").GetBoolean());
        Assert.Equal(3000m, way.GetProperty("outstanding").GetDecimal());
        Assert.Equal("Commercial Bank", way.GetProperty("bankName").GetString());
        Assert.Equal("Pay class", way.GetProperty("classTitle").GetString());
        Assert.True(way.GetProperty("hasQr").GetBoolean());
        Assert.StartsWith("GB", way.GetProperty("reference").GetString());
        Assert.Equal(8, way.GetProperty("reference").GetString()!.Length);

        // One call tells the app which fees can be paid this way, so a Pay button is only ever shown where it leads somewhere.
        Assert.Equal(learner.EnrolmentId, (await Get(student, "/api/payments/mine")).EnumerateArray().Single().GetGuid());

        // The teacher's QR reaches their own student.
        var qr = await student.GetAsync($"/api/payments/mine/{learner.EnrolmentId}/qr");
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);

        // Someone else's fee is not theirs to look at, whatever they name.
        var other = await Enrol(teacher.Client, classId, "Nuwan");
        Assert.Equal("enrolment_not_found", await Code(await student.GetAsync($"/api/payments/mine/{other.EnrolmentId}?month={Month()}")));
        Assert.Equal("enrolment_not_found", await Code(await student.GetAsync($"/api/payments/mine/{other.EnrolmentId}/qr")));
    }

    [Fact]
    public async Task A_student_says_they_have_paid_sends_a_slip_and_the_teacher_sees_it()
    {
        var teacher = await ProTeacher("Shanthi");
        var classId = await ClassOf(teacher.Client, "Slip class", fee: 1500m);
        var learner = await Enrol(teacher.Client, classId, "Dilani");
        await MakeDue(teacher.Client, learner.EnrolmentId);
        await SaveDetails(teacher.Client);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");

        var claim = await Claim(student, $"/api/payments/mine/{learner.EnrolmentId}/claims", 1500m, "Paid from my father's account");
        Assert.Equal("Open", claim.GetProperty("status").GetString());
        Assert.False(claim.GetProperty("hasSlip").GetBoolean());
        var claimId = claim.GetProperty("id").GetGuid();

        Assert.Equal("slip_invalid", await Code(await Upload(student, $"/api/payments/claims/{claimId}/slip", Encoding.UTF8.GetBytes("no"))));
        Assert.True((await Json(await Upload(student, $"/api/payments/claims/{claimId}/slip", Jpeg()))).GetProperty("hasSlip").GetBoolean());

        var waiting = (await Get(teacher.Client, "/api/payments/claims")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == claimId);
        Assert.Equal("Dilani Silva", waiting.GetProperty("fullName").GetString());
        Assert.Equal("Slip class", waiting.GetProperty("classTitle").GetString());
        Assert.Equal(1500m, waiting.GetProperty("outstanding").GetDecimal());
        Assert.Equal("Paid from my father's account", waiting.GetProperty("note").GetString());
        Assert.True(waiting.GetProperty("hasSlip").GetBoolean());
        Assert.Equal(claim.GetProperty("reference").GetString(), waiting.GetProperty("reference").GetString());

        var slip = await teacher.Client.GetAsync($"/api/payments/claims/{claimId}/slip");
        Assert.Equal(HttpStatusCode.OK, slip.StatusCode);
        Assert.Equal("image/jpeg", slip.Content.Headers.ContentType?.MediaType);

        // The teacher hears about it once, however many claims are waiting.
        Assert.Equal(1500m, (await Notifications(teacher.Client, "payment_claimed")).GetProperty("params").GetProperty("amount").GetDecimal());

        // A second claim waits behind the first rather than queueing up.
        var again = await student.PostAsJsonAsync($"/api/payments/mine/{learner.EnrolmentId}/claims", new { month = Month(), amount = 1500m });
        Assert.Equal("claim_open", await Code(again));
    }

    [Fact]
    public async Task Confirming_records_a_bank_transfer_with_a_receipt()
    {
        var teacher = await ProTeacher("Chamara");
        var classId = await ClassOf(teacher.Client, "Confirm class", fee: 2500m);
        var learner = await Enrol(teacher.Client, classId, "Tharindu");
        await MakeDue(teacher.Client, learner.EnrolmentId);
        await SaveDetails(teacher.Client);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");
        var claimId = (await Claim(student, $"/api/payments/mine/{learner.EnrolmentId}/claims", 2500m)).GetProperty("id").GetGuid();

        var confirmed = await Json(await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/confirm", new { }));
        Assert.Equal("Confirmed", confirmed.GetProperty("status").GetString());
        var receiptNo = confirmed.GetProperty("receiptNo").GetString();
        Assert.StartsWith("R-", receiptNo);

        // It is an ordinary payment: the fee is settled and the receipt says how the money came.
        var fees = await Get(teacher.Client, $"/api/fees/enrolments/{learner.EnrolmentId}");
        var payment = fees.GetProperty("payments").EnumerateArray().Single();
        Assert.Equal("BankTransfer", payment.GetProperty("method").GetString());
        Assert.Equal(receiptNo, payment.GetProperty("receiptNo").GetString());
        Assert.Equal(0m, fees.GetProperty("outstanding").GetDecimal());

        // The student is told the payment came in, as they would be for cash handed over.
        Assert.Equal(receiptNo, (await Notifications(student, "payment_received")).GetProperty("params").GetProperty("receiptNo").GetString());

        // Deciding twice is refused, and the fee cannot be claimed again once it is paid.
        Assert.Equal("claim_decided", await Code(await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/confirm", new { })));
        Assert.Equal("nothing_due", await Code(await student.PostAsJsonAsync($"/api/payments/mine/{learner.EnrolmentId}/claims", new { month = Month(), amount = 100m })));
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_tells_the_payer()
    {
        var teacher = await ProTeacher("Nalini");
        var classId = await ClassOf(teacher.Client, "Reject class", fee: 1000m);
        var learner = await Enrol(teacher.Client, classId, "Ishara");
        await MakeDue(teacher.Client, learner.EnrolmentId);
        await SaveDetails(teacher.Client);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");
        var claimId = (await Claim(student, $"/api/payments/mine/{learner.EnrolmentId}/claims", 1000m)).GetProperty("id").GetGuid();

        Assert.Equal("reason_required", await Code(await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/reject", new { reason = "  " })));
        var rejected = await Json(await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/reject", new { reason = "Nothing arrived in the account" }));
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("Nothing arrived in the account", rejected.GetProperty("rejectReason").GetString());

        // The fee is still owed, the payer is told why, and they may try again.
        Assert.Equal(1000m, (await Get(teacher.Client, $"/api/fees/enrolments/{learner.EnrolmentId}")).GetProperty("outstanding").GetDecimal());
        Assert.Equal("Nothing arrived in the account", (await Notifications(student, "payment_claim_rejected")).GetProperty("params").GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Created, (await student.PostAsJsonAsync($"/api/payments/mine/{learner.EnrolmentId}/claims", new { month = Month(), amount = 1000m })).StatusCode);
    }

    [Fact]
    public async Task A_claim_cannot_be_for_more_than_is_owed()
    {
        var teacher = await ProTeacher("Mahesh");
        var classId = await ClassOf(teacher.Client, "Amount class", fee: 1200m);
        var learner = await Enrol(teacher.Client, classId, "Ruwan");
        await MakeDue(teacher.Client, learner.EnrolmentId);
        await SaveDetails(teacher.Client);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");
        var url = $"/api/payments/mine/{learner.EnrolmentId}/claims";

        Assert.Equal("amount_invalid", await Code(await student.PostAsJsonAsync(url, new { month = Month(), amount = 0m })));
        Assert.Equal("amount_exceeds_outstanding", await Code(await student.PostAsJsonAsync(url, new { month = Month(), amount = 5000m })));
        Assert.Equal("fee_not_found", await Code(await student.PostAsJsonAsync(url, new { month = "not-a-month", amount = 100m })));

        // Part of the fee is fine: the teacher confirms what actually arrived.
        var claimId = (await Claim(student, url, 500m)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/confirm", new { amount = 400m })).StatusCode);
        Assert.Equal(800m, (await Get(teacher.Client, $"/api/fees/enrolments/{learner.EnrolmentId}")).GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task A_parent_pays_for_their_child_and_hears_the_answer()
    {
        var teacher = await ProTeacher("Gayan");
        var classId = await ClassOf(teacher.Client, "Parent class", fee: 1800m);
        var guardian = NewPhone();
        var learner = await Enrol(teacher.Client, classId, "Senuri", guardian);
        await MakeDue(teacher.Client, learner.EnrolmentId);
        await SaveDetails(teacher.Client);

        var invite = await Json(await teacher.Client.PostAsync($"/api/students/{learner.StudentId}/parent", null));
        var parent = await SignIn(guardian, invite.GetProperty("oneTimePassword").GetString()!, "Parent-pass-1");

        var way = await Get(parent, $"/api/payments/children/{learner.StudentId}/{learner.EnrolmentId}?month={Month()}");
        Assert.True(way.GetProperty("canPay").GetBoolean());
        Assert.Equal(learner.EnrolmentId, (await Get(parent, $"/api/payments/children/{learner.StudentId}")).EnumerateArray().Single().GetGuid());
        Assert.Equal(1800m, way.GetProperty("outstanding").GetDecimal());

        var claimId = (await Claim(parent, $"/api/payments/children/{learner.StudentId}/{learner.EnrolmentId}/claims", 1800m))
            .GetProperty("id").GetGuid();
        Assert.True((await Json(await Upload(parent, $"/api/payments/claims/{claimId}/slip", Jpeg()))).GetProperty("hasSlip").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/reject", new { reason = "The slip is for last month" })).StatusCode);
        var told = await Notifications(parent, "payment_claim_rejected");
        Assert.Equal("/family", told.GetProperty("link").GetString());

        // A parent reaches only the child a teacher linked to them.
        var stranger = await Enrol(teacher.Client, classId, "Hasini");
        Assert.Equal("enrolment_not_found", await Code(await parent.GetAsync($"/api/payments/children/{stranger.StudentId}/{stranger.EnrolmentId}?month={Month()}")));
    }

    [Fact]
    public async Task A_student_of_a_free_teacher_cannot_claim()
    {
        var free = await factory.NewTeacherAsync("Lakmal");
        var classId = await ClassOf(free.Client, "Free pay class");
        var learner = await Enrol(free.Client, classId, "Amal");
        await MakeDue(free.Client, learner.EnrolmentId);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");

        // The screen still answers, so the app can say payment is not set up rather than showing an error.
        Assert.False((await Get(student, $"/api/payments/mine/{learner.EnrolmentId}?month={Month()}")).GetProperty("canPay").GetBoolean());
        Assert.Empty((await Get(student, "/api/payments/mine")).EnumerateArray());
        Assert.Equal("bank_payments_off", await Code(await student.PostAsJsonAsync(
            $"/api/payments/mine/{learner.EnrolmentId}/claims", new { month = Month(), amount = 100m })));
    }

    [Fact]
    public async Task Only_the_payer_may_add_the_slip_and_not_after_a_decision()
    {
        var teacher = await ProTeacher("Dinesh");
        var classId = await ClassOf(teacher.Client, "Slip rules class", fee: 900m);
        var learner = await Enrol(teacher.Client, classId, "Yasas");
        await MakeDue(teacher.Client, learner.EnrolmentId);
        await SaveDetails(teacher.Client);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");
        var claimId = (await Claim(student, $"/api/payments/mine/{learner.EnrolmentId}/claims", 900m)).GetProperty("id").GetGuid();

        var someoneElse = await Enrol(teacher.Client, classId, "Bystander");
        var other = await SignIn(someoneElse.Phone, someoneElse.OneTimePassword, "Student-pass-1");
        Assert.Equal("claim_not_found", await Code(await Upload(other, $"/api/payments/claims/{claimId}/slip", Jpeg())));

        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/reject", new { reason = "Not found" })).StatusCode);
        Assert.Equal("claim_decided", await Code(await Upload(student, $"/api/payments/claims/{claimId}/slip", Jpeg())));
    }

    [Fact]
    public async Task Old_slips_are_cleared_months_after_the_decision_and_the_claim_stays()
    {
        var teacher = await ProTeacher("Kumari");
        var classId = await ClassOf(teacher.Client, "Cleanup class", fee: 700m);
        var learner = await Enrol(teacher.Client, classId, "Sachini");
        await MakeDue(teacher.Client, learner.EnrolmentId);
        await SaveDetails(teacher.Client);
        var student = await SignIn(learner.Phone, learner.OneTimePassword, "Student-pass-1");
        var claimId = (await Claim(student, $"/api/payments/mine/{learner.EnrolmentId}/claims", 700m)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Upload(student, $"/api/payments/claims/{claimId}/slip", Jpeg())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.Client.PostAsJsonAsync($"/api/payments/claims/{claimId}/confirm", new { })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        // A decision made today is still inside the keep period, so nothing is cleared yet.
        await scope.ServiceProvider.GetRequiredService<SlipCleaner>().RunOnceAsync(CancellationToken.None);
        Assert.NotNull((await db.Claims.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == claimId)).Slip);

        await db.Claims.IgnoreQueryFilters().Where(c => c.Id == claimId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.DecidedAt, DateTimeOffset.UtcNow - SlipCleaner.KeepFor - TimeSpan.FromDays(1)));
        await scope.ServiceProvider.GetRequiredService<SlipCleaner>().RunOnceAsync(CancellationToken.None);

        var kept = await db.Claims.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == claimId);
        Assert.Null(kept.Slip);
        Assert.Null(kept.SlipContentType);
        Assert.NotNull(kept.SlipRemovedAt);
        Assert.Equal(ClaimStatus.Confirmed, kept.Status);
        Assert.Equal(700m, kept.Amount);
    }

    [Fact]
    public void The_reference_is_the_same_for_one_fee_and_different_for_another()
    {
        var enrolment = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        Assert.Equal(PayReference.For(enrolment, "2027-01"), PayReference.For(enrolment, "2027-01"));
        Assert.NotEqual(PayReference.For(enrolment, "2027-01"), PayReference.For(enrolment, "2027-02"));
        Assert.NotEqual(PayReference.For(enrolment, "2027-01"), PayReference.For(other, "2027-01"));

        // Letters that are easily confused by hand are left out, so a code can be read off a phone and written on a slip.
        var code = PayReference.For(enrolment, "2027-01");
        Assert.DoesNotContain(code[2..], c => c is 'I' or 'L' or 'O' or 'U');
    }
}
