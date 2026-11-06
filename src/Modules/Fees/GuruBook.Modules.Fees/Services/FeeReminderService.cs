using GuruBook.Modules.Fees.Domain;
using GuruBook.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Services;

/// <summary>
/// Reminds people about fees on their own, from the hourly job, and only between 7 AM and 8 PM Sri Lanka time.
/// A student is told the day before a fee's period starts, on the due date, and then every week while it is unpaid (up to the
/// teacher's limit for the class). Each step goes out once per fee, and it stops when the fee is paid, becomes a free card, the student leaves
/// the class or the class is archived. A teacher can turn reminders off for a class or skip a student. A teacher is told once a month, from the 10th,
/// how many students in each class have not paid. Everything that goes out is noted in the reminder log.
/// </summary>
public sealed class FeeReminderService(
    FeesDbContext db, IClassRoster roster, IClassDirectory classes, INotifier notifier, IMessageRouter messages,
    IStudentContacts contacts, ITeacherDirectory teachers, TimeProvider clock)
{
    /// <summary>The day of the month the teacher's unpaid-fees reminder goes out, in Sri Lanka time.</summary>
    public const int TeacherReminderDay = 10;

    /// <summary>Nothing is sent before this hour or from the quiet hour on (Sri Lanka time).</summary>
    public const int FirstHour = 7;
    public const int QuietHour = 20;

    /// <summary>"Every week until paid" stops after this many weeks, so an old debt does not buzz a phone for years.</summary>
    public const int MaxWeeks = 26;

    public const string Before = "before";
    public const string Due = "due";
    public const string Late = "late";

    /// <summary>True between 8 PM and 7 AM, when nothing is sent.</summary>
    public static bool IsQuietHour(int hour) => hour < FirstHour || hour >= QuietHour;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var local = clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330));
        if (IsQuietHour(local.Hour))
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(local.DateTime);
        var current = FeeMonths.Of(today);
        var bases = await roster.FeeBasisForAllTeachersAsync(ct);
        if (bases.Count == 0)
        {
            return 0;
        }

        var ids = bases.Select(b => b.EnrolmentId).ToList();
        var oldest = FeeMonths.Add(current, -(MaxWeeks / 4 + 2));
        var dues = await db.Dues.IgnoreQueryFilters()
            .Where(d => d.AmountDue > 0 && ids.Contains(d.EnrolmentId) && d.LastMonth.CompareTo(oldest) >= 0)
            .ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. dues.Select(d => d.Id)], ct);
        var unpaid = dues.Where(d => paid.GetValueOrDefault(d.Id) < d.AmountDue).ToList();
        if (unpaid.Count == 0)
        {
            return 0;
        }

        var titles = await classes.FindManyInAnyTenantAsync(unpaid.Select(d => d.ClassId).Distinct(), ct);
        var requests = new List<NotifyRequest>();
        var log = new List<FeeReminder>();
        await AddStudentRemindersAsync(unpaid, paid, bases, titles, today, requests, log, ct);
        AddTeacherSummaries(unpaid.Where(d => d.Covers(current)), titles, current, today, requests);

        await notifier.NotifyAsync(requests, ct);
        await SaveLogAsync(log, ct);
        return requests.Count;
    }

    private async Task AddStudentRemindersAsync(
        List<FeeDue> unpaid, IReadOnlyDictionary<Guid, decimal> paid, IReadOnlyList<FeeBasis> bases, IReadOnlyDictionary<Guid, ClassSummary> titles,
        DateOnly today, List<NotifyRequest> requests, List<FeeReminder> log, CancellationToken ct)
    {
        var byEnrolment = bases.ToDictionary(b => b.EnrolmentId);
        var classIds = unpaid.Select(d => d.ClassId).Distinct().ToList();
        var enrolmentIds = unpaid.Select(d => d.EnrolmentId).Distinct().ToList();
        var dueIds = unpaid.Select(d => d.Id).ToList();

        var settings = await db.ReminderSettings.IgnoreQueryFilters().Where(s => classIds.Contains(s.ClassId)).ToDictionaryAsync(s => s.ClassId, ct);
        var skipped = (await db.ReminderSkips.IgnoreQueryFilters().Where(s => enrolmentIds.Contains(s.EnrolmentId)).Select(s => s.EnrolmentId).ToListAsync(ct)).ToHashSet();
        var sent = (await db.Reminders.IgnoreQueryFilters().Where(r => dueIds.Contains(r.DueId)).Select(r => new { r.DueId, r.Step, r.Number }).ToListAsync(ct))
            .Select(r => (r.DueId, r.Step, r.Number)).ToHashSet();

        var rules = new Dictionary<(Guid Teacher, string Event), MessageRule>();
        var paying = new Dictionary<(Guid Teacher, string Event), List<Owing>>();
        foreach (var due in unpaid)
        {
            if (!byEnrolment.TryGetValue(due.EnrolmentId, out var basis) || skipped.Contains(due.EnrolmentId)
                || !titles.TryGetValue(due.ClassId, out var cls) || cls.IsArchived)
            {
                continue;
            }

            settings.TryGetValue(due.ClassId, out var setting);
            if (setting is { Enabled: false } || StepFor(due, today, setting?.WeeklyLimit) is not { } step || sent.Contains((due.Id, step.Step, step.Number)))
            {
                continue;
            }

            var name = EventOf(step.Step);
            var key = (due.TenantId, name);
            if (!rules.TryGetValue(key, out var rule))
            {
                rule = await messages.RuleForAsync(due.TenantId, name, ct);
                rules[key] = rule;
            }

            // Off means the fee is not chased at all: not in the app either, because the teacher said not to.
            if (!rule.On)
            {
                continue;
            }

            var owing = due.AmountDue - paid.GetValueOrDefault(due.Id);
            var kind = step.Step switch { Before => "fee_before", Due => "fee_due_today", _ => "fee_overdue" };

            // A student who has never signed in still has a parent with a phone, so a paid reminder goes out without an account to show it in.
            if (basis.UserId is { } student)
            {
                requests.Add(new NotifyRequest(
                    student, kind,
                    new
                    {
                        classTitle = cls.Title, amount = owing, month = due.Month, lastMonth = due.LastMonth, dueDate = due.DueDate.ToString("yyyy-MM-dd"),
                        daysLate = Math.Max(0, today.DayNumber - due.DueDate.DayNumber), weeks = step.Number,
                    },
                    Link: "/welcome", OnceKey: $"remind:{due.Id}:{step.Step}:{step.Number}"));
            }
            log.Add(new FeeReminder
            {
                TenantId = due.TenantId, DueId = due.Id, EnrolmentId = due.EnrolmentId, ClassId = due.ClassId,
                Step = step.Step, Number = step.Number, Channel = ChannelOf(rule.Way),
            });

            if (MessageWays.IsPaid(rule.Way))
            {
                (paying.TryGetValue(key, out var list) ? list : paying[key] = []).Add(
                    new Owing(basis, cls.Title, due.Month, owing, $"{name}:{due.Id}:{step.Step}:{step.Number}"));
            }
        }

        await SendPaidAsync(paying, ct);
    }

    /// <summary>A fee a parent is about to hear about, with what it will say.</summary>
    private sealed record Owing(FeeBasis Basis, string ClassTitle, string Month, decimal Amount, string DedupKey);

    /// <summary>
    /// The WhatsApp messages or texts for the teachers who asked for them. The names are looked up once per teacher, and only for the teachers
    /// who pay for messages, so an ordinary run asks nothing extra of the database.
    /// </summary>
    private async Task SendPaidAsync(Dictionary<(Guid Teacher, string Event), List<Owing>> paying, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        var firstNames = new Dictionary<Guid, Dictionary<Guid, string>>();
        foreach (var ((teacher, name), items) in paying)
        {
            if (!names.TryGetValue(teacher, out var teacherName))
            {
                names[teacher] = teacherName = await teachers.FindNameAsync(teacher, ct) ?? "";
            }

            if (!firstNames.TryGetValue(teacher, out var first))
            {
                var found = await contacts.FindForTeacherAsync(teacher, paying.Where(p => p.Key.Teacher == teacher).SelectMany(p => p.Value).Select(o => o.Basis.StudentId), ct);
                firstNames[teacher] = first = found.ToDictionary(f => f.Key, f => f.Value.FirstName);
            }

            var drafts = items.Select(item => new MessageDraft(
                item.Basis.StudentId,
                [
                    first.GetValueOrDefault(item.Basis.StudentId, item.Basis.FullName), item.ClassTitle, MessageTexts.MonthLabel(item.Month),
                    MessageTexts.Lkr(item.Amount), teacherName,
                ],
                item.DedupKey)).ToList();
            await messages.SendForAsync(teacher, name, drafts, ct);
        }
    }

    /// <summary>Which of the three fee messages a step is, so the teacher's choice for it is the one that is used.</summary>
    private static string EventOf(string step) => step switch
    {
        Before => MessageEvents.FeeBefore,
        Due => MessageEvents.FeeDueToday,
        _ => MessageEvents.FeeOverdue,
    };

    /// <summary>How the reminder log names the way it went out.</summary>
    private static string ChannelOf(string way) => way switch
    {
        MessageWays.Sms => "Sms",
        MessageWays.WhatsApp or MessageWays.WhatsAppThenSms => "WhatsApp",
        _ => "App",
    };

    /// <summary>
    /// The one reminder a fee is due today, if any: the day before its period starts, then on the due date (or in the six days after, for a
    /// run that was missed), then once for each full week since. Only the current week is ever sent, so a long gap never sends a pile at once.
    /// </summary>
    internal static (string Step, int Number)? StepFor(FeeDue due, DateOnly today, int? weeklyLimit)
    {
        var start = new DateOnly(int.Parse(due.Month[..4]), int.Parse(due.Month[5..7]), 1);
        if (today == start.AddDays(-1) && today < due.DueDate)
        {
            return (Before, 0);
        }

        var late = today.DayNumber - due.DueDate.DayNumber;
        if (late < 0)
        {
            return null;
        }

        if (late < 7)
        {
            return (Due, 0);
        }

        var week = late / 7;
        return week <= Math.Min(weeklyLimit ?? MaxWeeks, MaxWeeks) ? (Late, week) : null;
    }

    private void AddTeacherSummaries(
        IEnumerable<FeeDue> unpaidNow, IReadOnlyDictionary<Guid, ClassSummary> titles, string current, DateOnly today, List<NotifyRequest> requests)
    {
        if (today.Day < TeacherReminderDay)
        {
            return;
        }

        foreach (var teacher in unpaidNow.GroupBy(d => d.TenantId))
        {
            var perClass = teacher.GroupBy(d => d.ClassId)
                .Select(g => new { classId = g.Key, title = titles.TryGetValue(g.Key, out var c) ? c.Title : "", count = g.Select(d => d.EnrolmentId).Distinct().Count() })
                .OrderBy(c => c.title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            requests.Add(new NotifyRequest(
                teacher.Key, "fees_unpaid", new { month = current, classes = perClass },
                Link: perClass.Count == 1 ? $"/fees?classId={perClass[0].classId}" : "/fees?classId=all", OnceKey: $"unpaid:{current}"));
        }
    }

    private async Task SaveLogAsync(List<FeeReminder> log, CancellationToken ct)
    {
        if (log.Count == 0)
        {
            return;
        }

        try
        {
            db.Reminders.AddRange(log);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another run noted the same step a moment ago, so it has gone out once already.
            db.ChangeTracker.Clear();
        }
    }
}
