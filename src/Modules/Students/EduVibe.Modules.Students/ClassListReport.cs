using EduVibe.Modules.Students.Domain;
using EduVibe.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace EduVibe.Modules.Students;

/// <summary>The Excel report of a class list: each active student with contact, school, grade and fee plan, by index number.</summary>
internal sealed class ClassListReport(StudentsDbContext db, IClassDirectory classes) : IReportSource
{
    public string Key => "class-list";

    public async Task<ReportResult> BuildAsync(IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
    {
        if (!query.TryGetValue("classId", out var text) || !Guid.TryParse(text, out var classId) ||
            await classes.FindAsync(classId, cancellationToken) is not { } klass)
        {
            return ReportResult.Fail("class_not_found");
        }

        var people = await db.Enrolments.Include(e => e.Student)
            .Where(e => e.ClassId == classId && e.Status == EnrolmentStatus.Active)
            .OrderBy(e => e.IndexNumber)
            .ToListAsync(cancellationToken);
        var rows = people
            .Select(e => (IReadOnlyList<object?>)
            [
                e.IndexNumber, e.Student.FullName, e.Student.Phone, e.Student.GuardianName, e.Student.GuardianPhone,
                e.Student.School, e.Student.Grade, PlanText(e.FeePlan), e.FeePlan == FeePlan.Discount ? e.DiscountAmount : null, e.JoinedOn,
            ])
            .ToList();
        return ReportResult.Ok(new ReportTable(
            $"Class list {klass.Title}",
            ["Index", "Student", "Phone", "Guardian", "Guardian phone", "School", "Grade", "Fee plan", "Discount (LKR)", "Joined"],
            rows));
    }

    private static string PlanText(FeePlan plan) => plan switch
    {
        FeePlan.FreeCard => "Free card",
        FeePlan.Discount => "Discount",
        _ => "Full fee",
    };
}
