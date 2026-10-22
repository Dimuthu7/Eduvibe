using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using GuruBook.Shared.Modules;

namespace GuruBook.Modules.Reports;

/// <summary>
/// Writes a report table as a plain .xlsx file (one sheet, bold frozen header row, sensible column widths). An .xlsx is a zip
/// of a few XML files, so this needs no library. Text goes in as inline strings, which Excel never evaluates as a formula.
/// </summary>
public static class XlsxWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int MaxSheetNameLength = 31;
    private const int MinWidth = 8;
    private const int MaxWidth = 50;
    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    public static byte[] Write(ReportTable table)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", RootRelationships);
            Add(zip, "xl/workbook.xml", Workbook(SheetName(table.Title)));
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, "xl/worksheets/sheet1.xml", Sheet(table));
        }

        return stream.ToArray();
    }

    /// <summary>The title cleaned to a legal sheet name: no [ ] : * ? / \ and at most 31 characters.</summary>
    public static string SheetName(string title)
    {
        var clean = new string([.. title.Where(c => !"[]:*?/\\".Contains(c))]).Trim();
        if (clean.Length > MaxSheetNameLength)
        {
            clean = clean[..MaxSheetNameLength].TrimEnd();
        }

        return clean.Length == 0 ? "Report" : clean;
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Sheet(ReportTable table)
    {
        var builder = new StringBuilder(Declaration);
        builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        builder.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");

        builder.Append("<cols>");
        for (var column = 0; column < table.Headers.Count; column++)
        {
            var longest = table.Rows.Select(r => column < r.Count ? Text(r[column]).Length : 0).Append(table.Headers[column].Length).Max();
            var width = Math.Clamp(longest + 2, MinWidth, MaxWidth);
            builder.Append(CultureInfo.InvariantCulture, $"<col min=\"{column + 1}\" max=\"{column + 1}\" width=\"{width}\" customWidth=\"1\"/>");
        }

        builder.Append("</cols><sheetData>");
        builder.Append("<row r=\"1\">");
        for (var column = 0; column < table.Headers.Count; column++)
        {
            builder.Append(Cell(column, 1, table.Headers[column], style: 1));
        }

        builder.Append("</row>");

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = r + 2;
            builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{row}\">");
            for (var column = 0; column < table.Rows[r].Count; column++)
            {
                builder.Append(Cell(column, row, table.Rows[r][column], style: 0));
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string Cell(int column, int row, object? value, int style)
    {
        var reference = ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
        switch (value)
        {
            case null:
                return string.Empty;
            case decimal money:
                // Without trailing zeros: the database may hand back 4000.00 for 4000.
                return $"<c r=\"{reference}\"><v>{money.ToString("0.############################", CultureInfo.InvariantCulture)}</v></c>";
            case int or long or short or byte or double or float:
                return $"<c r=\"{reference}\"><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>";
            case bool flag:
                return $"<c r=\"{reference}\" t=\"b\"><v>{(flag ? 1 : 0)}</v></c>";
            case DateOnly date:
                // Excel counts days from 1899-12-30.
                return $"<c r=\"{reference}\" s=\"2\"><v>{date.DayNumber - new DateOnly(1899, 12, 30).DayNumber}</v></c>";
            default:
                var styleAttribute = style > 0 ? $" s=\"{style}\"" : string.Empty;
                return $"<c r=\"{reference}\" t=\"inlineStr\"{styleAttribute}><is><t xml:space=\"preserve\">{SecurityElement.Escape(Text(value))}</t></is></c>";
        }
    }

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>0 is A, 25 is Z, 26 is AA.</summary>
    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    private const string ContentTypes =
        Declaration +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RootRelationships =
        Declaration +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string WorkbookRelationships =
        Declaration +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    private static string Workbook(string sheetName) =>
        Declaration +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        $"<sheets><sheet name=\"{SecurityElement.Escape(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    // Style 0 plain, 1 bold (header), 2 date (yyyy-mm-dd).
    private const string Styles =
        Declaration +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/></numFmts>" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"3\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";
}
