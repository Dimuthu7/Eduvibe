using EduVibe.Shared.Modules;

namespace EduVibe.Modules.Fees.Services;

/// <summary>The Excel report of one class's fees for one month: each student's status, due, paid and outstanding, with a total row.</summary>
internal sealed class FeeStatusReport(ClassMonthService months, IClassDirectory classes) : IReportSource
{
    public string Key => "fee-status";

    public async Task<ReportResult> BuildAsync(IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
    {
        if (!query.TryGetValue("classId", out var text) || !Guid.TryParse(text, out var classId))
        {
            return ReportResult.Fail("class_not_found");
        }

        var result = await months.BuildAsync(classId, query.GetValueOrDefault("month") is { Length: > 0 } m ? m : null, cancellationToken);
        if (result.Value is not { } data)
        {
            return ReportResult.Fail(result.Error!);
        }

        var title = (await classes.FindAsync(classId, cancellationToken))?.Title ?? "Class";
        var rows = data.Rows
            .Select(r => (IReadOnlyList<object?>)[r.IndexNumber, r.FullName, Label(r.Status), r.Due, r.Paid, r.Status == "FreeCard" ? 0m : r.Due - r.Paid])
            .ToList();
        rows.Add(["", "Total", "", data.Totals.Due, data.Totals.Collected, data.Totals.Outstanding]);
        return ReportResult.Ok(new ReportTable(
            $"Fees {title} {data.Month}",
            ["Index", "Student", "Status", "Due (LKR)", "Paid (LKR)", "Outstanding (LKR)"],
            rows));
    }

    private static string Label(string status) => status switch
    {
        "PartlyPaid" => "Part paid",
        "FreeCard" => "Free card",
        _ => status,
    };
}
