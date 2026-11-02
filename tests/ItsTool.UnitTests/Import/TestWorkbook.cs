using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace ItsTool.Tests.Shared;

/// <summary>
/// Builds a small .xlsx in memory, laid out like FNRC's Excel risk register, for the import tests. Text goes in the
/// shared strings table, as Excel writes it, except where a test asks for an inline string. Shared with the
/// integration tests (linked in their project file).
/// </summary>
public static class TestWorkbook
{
    /// <summary>The register's heading rows 3 and 4 (columns A-X).</summary>
    public static readonly (string Cell, string Value)[] RegisterHeadings =
    {
        ("A1", "FNRCs' Risk Register"),
        ("A3", "ID"), ("B3", "Risk Scenario"), ("C3", "Threat "), ("D3", "Possible Cause /Vulnerabilities"), ("E3", "Existing Control"),
        ("F3", "Expected Impact"), ("I3", "Likelihood"), ("J3", "Risk Value\\ Rating"), ("K3", "Risk Level / "), ("L3", "Risk Treatment option"),
        ("M3", "Recommended Controls"), ("N3", "ISO 27001 Controls "), ("O3", "Risk Owner "), ("P3", "Expexted Date"), ("Q3", "Status"),
        ("R3", "Treated Impact"), ("U3", "Treated Likelihood"), ("V3", "Risk Rating"), ("W3", " Risk level"), ("X3", "Status "),
        ("F4", "C"), ("G4", "I"), ("H4", "A"), ("R4", "C"), ("S4", "I"), ("T4", "A")
    };

    /// <summary>A register row: the cells of row <paramref name="row"/>, from columns A to U.</summary>
    public static IEnumerable<(string Cell, string Value)> RegisterRow(int row, string id, string title, string c, string i, string a, string likelihood,
        string treatment = "Mitigate", string annex = "", string owner = "", string date = "", string status = "", string residual = "")
    {
        var cells = new List<(string, string)>
        {
            ($"A{row}", id), ($"B{row}", title), ($"C{row}", "Threat"), ($"D{row}", "Cause"), ($"E{row}", "Existing"),
            ($"F{row}", c), ($"G{row}", i), ($"H{row}", a), ($"I{row}", likelihood), ($"J{row}", "99"), ($"K{row}", "Medum Risk"),
            ($"L{row}", treatment), ($"M{row}", "Recommended"), ($"N{row}", annex), ($"O{row}", owner), ($"P{row}", date), ($"Q{row}", status)
        };
        var ratings = residual.Split(',', StringSplitOptions.RemoveEmptyEntries);
        for (var n = 0; n < ratings.Length; n++) cells.Add(($"{(char)('R' + n)}{row}", ratings[n]));
        return cells.Where(c => c.Item2 != "");
    }

    public static MemoryStream Build(IEnumerable<(string Cell, string Value)> cells, string sheetName = "Risk Assessment", ISet<string>? inline = null)
    {
        var shared = new List<string>();
        var rows = cells
            .Select(c => (Row: int.Parse(new string(c.Cell.SkipWhile(char.IsLetter).ToArray()), CultureInfo.InvariantCulture), c.Cell, c.Value))
            .GroupBy(c => c.Row).OrderBy(g => g.Key);

        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        foreach (var row in rows)
        {
            sheet.Append($"<row r=\"{row.Key}\">");
            foreach (var (_, cell, value) in row.OrderBy(c => c.Cell.Length).ThenBy(c => c.Cell))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    sheet.Append($"<c r=\"{cell}\"><v>{value}</v></c>");
                else if (inline?.Contains(cell) == true)
                    sheet.Append($"<c r=\"{cell}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>");
                else
                {
                    shared.Add(value);
                    sheet.Append($"<c r=\"{cell}\" t=\"s\"><v>{shared.Count - 1}</v></c>");
                }
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Add(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                $"<sheets><sheet name=\"Doc Info \" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"{SecurityElement.Escape(sheetName)}\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
            Add(zip, "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Version 12</t></is></c></row></sheetData></worksheet>");
            Add(zip, "xl/worksheets/sheet2.xml", sheet.ToString());
            Add(zip, "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                string.Concat(shared.Select(s => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(s)}</t></si>")) + "</sst>");
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
