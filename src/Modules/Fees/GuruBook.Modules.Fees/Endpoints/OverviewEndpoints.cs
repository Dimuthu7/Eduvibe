using GuruBook.Modules.Fees.Domain;
using GuruBook.Modules.Fees.Services;
using GuruBook.Shared.Modules;
using GuruBook.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GuruBook.Modules.Fees.Endpoints;

public sealed record ClassOverviewDto(Guid ClassId, string Title, bool IsArchived, MonthTotalsDto Totals);

public sealed record FeeOverviewDto(string Month, MonthTotalsDto Totals, ClassOverviewDto[] Classes);

public sealed record UnpaidAttendeeDto(
    Guid EnrolmentId, Guid ClassId, string ClassTitle, int IndexNumber, string FullName, int Attended, decimal Due, decimal Paid, decimal Outstanding);

public sealed record UnpaidAttendeesDto(string Month, decimal Outstanding, UnpaidAttendeeDto[] Rows);

/// <summary>Fees across all of a teacher's classes: the month's totals per class, and who came to class but has not paid.</summary>
internal static class OverviewEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var fees = group.MapGroup("").RequireAuthorization(Policies.Teacher);
        fees.MapGet("/overview", Overview);
        fees.MapGet("/unpaid-attendees", UnpaidAttendees);
    }

    // This month (or the one asked for) for every class, with a grand total.
    private static async Task<IResult> Overview(
        string? month, FeesDbContext db, FeeDueService dues, IClassDirectory classes, TimeProvider clock, CancellationToken ct)
    {
        var current = FeeMonths.Current(clock);
        if (ClassMonthService.CheckMonth(month, current) is { } problem)
        {
            return Problems.Reject(problem);
        }

        var chosen = month ?? current;
        await dues.EnsureForCurrentTeacherAsync([chosen], ct);
        var rows = await db.Dues.Where(d => d.Month == chosen).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. rows.Select(r => r.Id)], ct);
        var titles = await classes.FindManyAsync(rows.Select(r => r.ClassId).Distinct(), ct);

        (string Status, decimal Due, decimal Paid) Line(FeeDue d) =>
            (FeeMonths.StatusOf(d.AmountDue, paid.GetValueOrDefault(d.Id)).ToString(), d.AmountDue, paid.GetValueOrDefault(d.Id));

        var perClass = rows
            .Where(r => titles.ContainsKey(r.ClassId))
            .GroupBy(r => r.ClassId)
            .Select(g => new ClassOverviewDto(g.Key, titles[g.Key].Title, titles[g.Key].IsArchived, ClassMonthService.Totals(g.Select(Line))))
            .OrderBy(c => c.IsArchived)
            .ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var overall = ClassMonthService.Totals(rows.Where(r => titles.ContainsKey(r.ClassId)).Select(Line));
        return Results.Ok(new FeeOverviewDto(chosen, overall, perClass));
    }

    // Students who came to at least one class in the month and still owe for it, so the teacher knows whom to ask.
    private static async Task<IResult> UnpaidAttendees(
        string? month, Guid? classId, FeesDbContext db, FeeDueService dues, IClassDirectory classes, IClassRoster roster,
        IAttendanceLookup attendance, TimeProvider clock, CancellationToken ct)
    {
        var current = FeeMonths.Current(clock);
        if (ClassMonthService.CheckMonth(month, current) is { } problem)
        {
            return Problems.Reject(problem);
        }

        if (classId is { } id && await classes.FindAsync(id, ct) is null)
        {
            return Problems.Reject("class_not_found", StatusCodes.Status404NotFound);
        }

        var chosen = month ?? current;
        if (classId is { } one)
        {
            await dues.EnsureForClassAsync(one, [chosen], ct);
        }
        else
        {
            await dues.EnsureForCurrentTeacherAsync([chosen], ct);
        }

        var first = DateOnly.ParseExact(chosen + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var attended = await attendance.AttendedCountsAsync(first, first.AddMonths(1).AddDays(-1), classId, ct);
        if (attended.Count == 0)
        {
            return Results.Ok(new UnpaidAttendeesDto(chosen, 0m, []));
        }

        var ids = attended.Keys.ToList();
        var due = await db.Dues.Where(d => d.Month == chosen && ids.Contains(d.EnrolmentId) && d.AmountDue > 0m).ToListAsync(ct);
        var paid = await FeeLedger.PaidByDueAsync(db, [.. due.Select(d => d.Id)], ct);
        var owing = due.Where(d => d.AmountDue - paid.GetValueOrDefault(d.Id) > 0m).ToList();
        var people = await roster.FindEnrolmentsAsync(owing.Select(d => d.EnrolmentId), ct);
        var titles = await classes.FindManyAsync(owing.Select(d => d.ClassId).Distinct(), ct);

        var lines = owing
            .Where(d => people.ContainsKey(d.EnrolmentId) && titles.ContainsKey(d.ClassId))
            .Select(d =>
            {
                var got = paid.GetValueOrDefault(d.Id);
                var p = people[d.EnrolmentId];
                return new UnpaidAttendeeDto(d.EnrolmentId, d.ClassId, titles[d.ClassId].Title, p.IndexNumber, p.FullName, attended[d.EnrolmentId], d.AmountDue, got, d.AmountDue - got);
            })
            .OrderBy(l => l.ClassTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.IndexNumber)
            .ToArray();
        return Results.Ok(new UnpaidAttendeesDto(chosen, lines.Sum(l => l.Outstanding), lines));
    }
}
