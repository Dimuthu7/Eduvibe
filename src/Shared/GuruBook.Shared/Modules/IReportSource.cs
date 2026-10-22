namespace GuruBook.Shared.Modules;

/// <summary>A report as a table: a title (also the file name), the column headings and the rows. Cells are text, numbers or dates.</summary>
public sealed record ReportTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>A table, or the code of the problem with the request (such as "class_not_found").</summary>
public sealed record ReportResult(ReportTable? Table, string? Error)
{
    public static ReportResult Ok(ReportTable table) => new(table, null);

    public static ReportResult Fail(string error) => new(null, error);
}

/// <summary>
/// A report a module can produce for the signed-in teacher, as an Excel file. The module that owns the data
/// implements it and the Reports module turns the table into a file, so Reports never reads another module's tables.
/// </summary>
public interface IReportSource
{
    /// <summary>Name used in the address: /api/reports/{Key}.</summary>
    string Key { get; }

    /// <summary>Builds the report from the request's query values (for example classId and month).</summary>
    Task<ReportResult> BuildAsync(IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default);
}
