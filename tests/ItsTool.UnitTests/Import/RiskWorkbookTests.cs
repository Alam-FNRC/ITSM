using System.IO.Compression;
using System.Text;
using ItsTool.Domain.Entities.Risks;
using ItsTool.Infrastructure.Import;
using ItsTool.Tests.Shared;

namespace ItsTool.UnitTests.Import;

public class RiskWorkbookTests
{
    private static MemoryStream Register(params IEnumerable<(string, string)>[] rows) =>
        TestWorkbook.Build(TestWorkbook.RegisterHeadings.Concat(rows.SelectMany(r => r)));

    [Fact]
    public void Rows_are_read_by_their_headings_and_the_excel_score_is_ignored()
    {
        using var file = Register(
            TestWorkbook.RegisterRow(5, "1", "Single point of failure of the database", "3", "5", "5", "4",
                annex: "A5.30", owner: "Head of IT Section", date: "Q4 2024", status: "Closed", residual: "3,4,5,1"),
            new[] { ("A6", "15") }); // a numbered row with nothing in it, as the Excel has down to row 43

        var row = Assert.Single(RiskWorkbook.Parse(file));

        Assert.Equal((5, 1, "Single point of failure of the database"), (row.RowNumber, row.Number, row.Title));
        Assert.Equal(("Threat", "Cause", "Existing", "Recommended"), (row.Threat, row.Vulnerability, row.ExistingControls, row.RecommendedControls));
        Assert.Equal((3, 5, 5, 4), (row.Confidentiality!.Value, row.Integrity!.Value, row.Availability!.Value, row.Likelihood!.Value));
        Assert.Equal((3, 4, 5, 1), (row.ResidualConfidentiality!.Value, row.ResidualIntegrity!.Value, row.ResidualAvailability!.Value, row.ResidualLikelihood!.Value));
        Assert.Equal(new[] { "5.30" }, row.AnnexAControls);
        Assert.Equal(("Head of IT Section", new DateOnly(2024, 12, 31), RiskStatus.Done, RiskTreatment.Mitigate), (row.Owner, row.TargetDate, row.Status, row.Treatment));
        Assert.Empty(row.Warnings);
    }

    [Fact]
    public void Values_that_cannot_be_read_become_warnings_instead_of_guesses()
    {
        using var file = Register(
            TestWorkbook.RegisterRow(5, "2", "No offline backup", "3", "3", "4", "4", annex: "A.5.23, A.8.13", date: "Q432025", status: "Planned"),
            TestWorkbook.RegisterRow(6, "3", "Low awareness", "4", "3", "3", "2", treatment: "Reduce", annex: "A.6.3; A9.99", date: "2026-06-30", status: "Someday", residual: "2,2"),
            TestWorkbook.RegisterRow(7, "4", "Not rated yet", "", "", "", "", treatment: "Ignore"));

        var rows = RiskWorkbook.Parse(file);

        Assert.Equal(new[] { "5.23", "8.13" }, rows[0].AnnexAControls);
        Assert.Null(rows[0].TargetDate);
        Assert.Equal(new[] { "date:Q432025" }, rows[0].Warnings);
        Assert.Equal(RiskStatus.Treating, rows[0].Status);

        Assert.Equal((RiskTreatment.Mitigate, new DateOnly(2026, 6, 30), RiskStatus.Open), (rows[1].Treatment, rows[1].TargetDate, rows[1].Status));
        Assert.Equal(new[] { "6.3" }, rows[1].AnnexAControls);
        Assert.Equal(new[] { "residual:", "control:A9.99", "status:Someday" }, rows[1].Warnings);
        Assert.Null(rows[1].ResidualConfidentiality);

        Assert.False(rows[2].HasRatings);
        Assert.Contains("treatment:Ignore", rows[2].Warnings);
    }

    [Fact]
    public void A_file_that_is_not_a_register_is_refused()
    {
        using var notZip = new MemoryStream(Encoding.UTF8.GetBytes("ID,Risk Scenario\n1,Test"));
        Assert.Throws<ArgumentException>(() => RiskWorkbook.Parse(notZip));

        using var otherSheet = TestWorkbook.Build(new[] { ("A1", "Asset"), ("B1", "Serial number") });
        Assert.Throws<ArgumentException>(() => RiskWorkbook.Parse(otherSheet));
    }

    [Fact]
    public void Shared_and_inline_strings_and_numbers_are_read_cell_by_cell()
    {
        using var file = TestWorkbook.Build(new[] { ("A1", "shared"), ("B1", "42"), ("C1", "inline & <odd>"), ("AA1", "far") }, inline: new HashSet<string> { "C1" });

        var sheet = XlsxReader.Read(file).Single(s => s.Name == "Risk Assessment");

        Assert.Equal(("shared", "42", "inline & <odd>", "far"), (sheet.Cell(1, 1), sheet.Cell(1, 2), sheet.Cell(1, 3), sheet.Cell(1, 27)));
        Assert.Equal(27, XlsxReader.ColumnOf("AA12"));
    }

    [Fact]
    public void A_part_larger_than_the_limit_is_not_read()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("xl/sharedStrings.xml").Open());
            writer.Write("<sst>");
            var chunk = new string('x', 1024 * 1024);
            for (var i = 0; i <= XlsxReader.MaxPartBytes / chunk.Length; i++) writer.Write(chunk);
            writer.Write("</sst>");
        }
        stream.Position = 0;

        Assert.Throws<ArgumentException>(() => XlsxReader.Read(stream));
    }
}
