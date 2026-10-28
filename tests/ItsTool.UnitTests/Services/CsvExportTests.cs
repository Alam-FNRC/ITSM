using ItsTool.Infrastructure.Services;
using Xunit;

namespace ItsTool.UnitTests.Services;

// Ticket titles and names in the export are typed by users; a spreadsheet must not run them as formulas
public class CsvExportTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"https://evil.example\",\"Open\")", "'=HYPERLINK(\"\"https://evil.example\"\",\"\"Open\"\")")]
    [InlineData("+1 555", "'+1 555")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Printer \"offline\"", "Printer \"\"offline\"\"")]
    [InlineData(null, "")]
    public void Cells_that_a_spreadsheet_would_run_are_kept_as_text(string? value, string expected)
    {
        Assert.Equal(expected, ReportService.EscapeCsv(value));
    }
}
