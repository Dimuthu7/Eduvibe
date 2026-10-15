using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Fees.Services;

/// <summary>
/// Tells people about this month's fees, from the same job that makes the dues. A student hears once when a fee they still owe has just been
/// made; a teacher hears once a month, from the 10th, how many students in each class have not paid. Asking again changes nothing,
/// because each notification has a key of its own.
/// </summary>
public sealed class FeeReminderService(FeesDbContext db, IClassRoster roster, IClassDirectory classes, INotifier notifier, TimeProvider clock)
{
    /// <summary>The day of the month the teacher's unpaid-fees reminder goes out, in Sri Lanka time.</summary>
    public const int TeacherReminderDay = 10;

    /// <summary>Dues older than this are not announced to students: they were made before the job could tell anyone.</summary>
    private static readonly TimeSpan JustMade = TimeSpan.FromDays(3);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var current = FeeMonths.Current(clock);
        var bases = await roster.FeeBasisForAllTeachersAsync(ct);
        if (bases.Count == 0)
        {
            return 0;
        }

        var ids = bases.Select(b => b.EnrolmentId).ToList();
        var dues = await db.Dues.IgnoreQueryFilters().Where(d => d.Month == current && d.AmountDue > 0 && ids.Contains(d.EnrolmentId)).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. dues.Select(d => d.Id)], ct);
        var unpaid = dues.Where(d => paid.GetValueOrDefault(d.Id) < d.AmountDue).ToList();
        if (unpaid.Count == 0)
        {
            return 0;
        }

        var byEnrolment = bases.ToDictionary(b => b.EnrolmentId);
        var titles = await classes.FindManyInAnyTenantAsync(unpaid.Select(d => d.ClassId).Distinct(), ct);
        var requests = new List<NotifyRequest>();

        var cutoff = clock.GetUtcNow() - JustMade;
        foreach (var due in unpaid.Where(d => d.CreatedAt >= cutoff))
        {
            if (byEnrolment[due.EnrolmentId].UserId is { } student)
            {
                requests.Add(new NotifyRequest(
                    student, "fee_due",
                    new { month = due.Month, classTitle = titles.TryGetValue(due.ClassId, out var c) ? c.Title : "", amount = due.AmountDue - paid.GetValueOrDefault(due.Id) },
                    Link: "/welcome", OnceKey: $"due:{due.Id}"));
            }
        }

        if (ColomboNow().Day >= TeacherReminderDay)
        {
            foreach (var teacher in unpaid.GroupBy(d => d.TenantId))
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

        await notifier.NotifyAsync(requests, ct);
        return requests.Count;
    }

    private DateTime ColomboNow() => clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime;
}
