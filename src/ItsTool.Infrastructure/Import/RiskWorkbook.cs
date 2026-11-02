using System.Globalization;
using System.Text.RegularExpressions;
using ItsTool.Application.Constants;
using ItsTool.Domain.Entities.Risks;

namespace ItsTool.Infrastructure.Import;

/// <summary>
/// Reads FNRC's Excel risk register ("FNRC ISMS Risk Assessment register"): the sheet whose heading row has
/// "Risk Scenario", with columns found by their headings, so a later version with moved columns still reads.
/// Scores and levels are never read; they are worked out again from the ratings.
/// </summary>
public static class RiskWorkbook
{
    /// <summary>One risk row. Ratings are null when missing or outside 1-5; such a row cannot be imported.</summary>
    public sealed record Row(
        int RowNumber, int? Number, string Title, string? Threat, string? Vulnerability, string? ExistingControls,
        int? Confidentiality, int? Integrity, int? Availability, int? Likelihood,
        RiskTreatment Treatment, string? RecommendedControls, IReadOnlyList<string> AnnexAControls,
        string? Owner, DateOnly? TargetDate, RiskStatus Status,
        int? ResidualConfidentiality, int? ResidualIntegrity, int? ResidualAvailability, int? ResidualLikelihood,
        IReadOnlyList<string> Warnings)
    {
        public bool HasRatings => Confidentiality.HasValue && Integrity.HasValue && Availability.HasValue && Likelihood.HasValue;
    }

    // Warnings travel as "kind:value" so the page can word them in the reader's language
    public const string BadRating = "rating";
    public const string BadDate = "date";
    public const string BadControl = "control";
    public const string BadTreatment = "treatment";
    public const string BadStatus = "status";
    public const string PartResidual = "residual";

    private static readonly Regex Quarter = new(@"^Q\s*([1-4])\s*[-/ ]?\s*(\d{4})$", RegexOptions.IgnoreCase);
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "d MMM yyyy", "MMM yyyy" };

    private sealed class Columns
    {
        public int Number, Title, Threat, Vulnerability, Existing, Impact, Likelihood, Treatment, Recommended, Annex, Owner, Date, Status, ResidualImpact, ResidualLikelihood;
    }

    /// <exception cref="ArgumentException">Not a workbook, or no sheet with the register's headings.</exception>
    public static IReadOnlyList<Row> Parse(Stream workbook)
    {
        foreach (var sheet in XlsxReader.Read(workbook))
        {
            foreach (var (rowNumber, cells) in sheet.Rows.OrderBy(r => r.Key))
            {
                if (!cells.Values.Any(v => Heading(v).Contains("risk scenario"))) continue;
                var columns = FindColumns(cells);
                if (columns.Title == 0 || columns.Impact == 0 || columns.Likelihood == 0) continue;
                // The C / I / A labels sit on the row under the merged "Expected Impact" heading
                var firstData = sheet.Cell(rowNumber + 1, columns.Impact).Trim().Equals("C", StringComparison.OrdinalIgnoreCase) ? rowNumber + 2 : rowNumber + 1;
                return sheet.Rows.Keys.Where(r => r >= firstData).OrderBy(r => r)
                    .Select(r => ReadRow(sheet, r, columns))
                    .Where(r => r != null).Select(r => r!).ToList();
            }
        }
        throw new ArgumentException("No sheet in this workbook has the risk register's headings (Risk Scenario, Expected Impact, Likelihood).");
    }

    private static string Heading(string text) => Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();

    private static Columns FindColumns(IReadOnlyDictionary<int, string> cells)
    {
        var columns = new Columns();
        foreach (var (column, raw) in cells.OrderBy(c => c.Key))
        {
            var h = Heading(raw);
            bool Take(ref int slot, bool matches) { if (slot != 0 || !matches) return false; slot = column; return true; }
            var after = h.Contains("treated") || h.Contains("residual");
            if (Take(ref columns.ResidualImpact, after && h.Contains("impact"))) continue;
            if (Take(ref columns.ResidualLikelihood, after && h.Contains("likelihood"))) continue;
            if (after) continue;
            if (Take(ref columns.Number, h is "id" or "no" or "no." or "#" or "ref")) continue;
            if (Take(ref columns.Title, h.Contains("risk scenario"))) continue;
            if (Take(ref columns.Threat, h.StartsWith("threat"))) continue;
            if (Take(ref columns.Vulnerability, h.Contains("cause") || h.Contains("vulnerabilit"))) continue;
            if (Take(ref columns.Existing, h.Contains("existing control"))) continue;
            if (Take(ref columns.Impact, h.Contains("impact"))) continue;
            if (Take(ref columns.Likelihood, h.Contains("likelihood"))) continue;
            if (Take(ref columns.Treatment, h.Contains("treatment"))) continue;
            if (Take(ref columns.Recommended, h.Contains("recommended"))) continue;
            if (Take(ref columns.Annex, h.Contains("27001") || h.Contains("annex"))) continue;
            if (Take(ref columns.Owner, h.Contains("owner"))) continue;
            if (Take(ref columns.Date, h.Contains("date"))) continue;
            // The first Status is the treatment's; the Excel's second, residual one is dropped (decided 2 Nov 2026)
            Take(ref columns.Status, h.StartsWith("status"));
        }
        return columns;
    }

    private static Row? ReadRow(XlsxReader.Sheet sheet, int row, Columns columns)
    {
        string? Text(int column) => column == 0 ? null : sheet.Cell(row, column) is { Length: > 0 } value ? value : null;
        var title = Text(columns.Title);
        if (title == null) return null;
        var warnings = new List<string>();

        int? Rating(int column)
        {
            var raw = Text(column);
            if (raw == null) return null;
            if (WholeNumber(raw) is { } value && RiskScore.IsRating(value)) return value;
            warnings.Add($"{BadRating}:{raw}");
            return null;
        }

        int? residualC = null, residualI = null, residualA = null, residualL = null;
        if (columns.ResidualImpact != 0)
        {
            residualC = Rating(columns.ResidualImpact);
            residualI = Rating(columns.ResidualImpact + 1);
            residualA = Rating(columns.ResidualImpact + 2);
        }
        if (columns.ResidualLikelihood != 0) residualL = Rating(columns.ResidualLikelihood);
        var residual = new[] { residualC, residualI, residualA, residualL };
        if (residual.Any(r => r.HasValue) && !residual.All(r => r.HasValue))
        {
            // Half a residual rating gives no score; leave it out rather than guess
            warnings.Add($"{PartResidual}:");
            residualC = residualI = residualA = residualL = null;
        }

        var controls = new List<string>();
        foreach (var token in (Text(columns.Annex) ?? string.Empty).Split(new[] { ',', ';', '/', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (AnnexAControls.Normalize(token) is { } code) { if (!controls.Contains(code)) controls.Add(code); }
            else warnings.Add($"{BadControl}:{token}");
        }

        return new Row(
            row, WholeNumber(Text(columns.Number)), title, Text(columns.Threat), Text(columns.Vulnerability), Text(columns.Existing),
            Rating(columns.Impact), Rating(columns.Impact + 1), Rating(columns.Impact + 2), Rating(columns.Likelihood),
            ReadTreatment(Text(columns.Treatment), warnings), Text(columns.Recommended), controls,
            Text(columns.Owner), ReadDate(Text(columns.Date), warnings), ReadStatus(Text(columns.Status), warnings),
            residualC, residualI, residualA, residualL, warnings);
    }

    private static int? WholeNumber(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value == Math.Floor(value) && Math.Abs(value) < int.MaxValue
            ? (int)value : null;

    private static RiskTreatment ReadTreatment(string? raw, List<string> warnings)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case null or "": return RiskTreatment.Mitigate;
            case "mitigate" or "reduce" or "modify" or "treat": return RiskTreatment.Mitigate;
            case "avoid": return RiskTreatment.Avoid;
            case "accept" or "retain": return RiskTreatment.Accept;
            case "transfer" or "share": return RiskTreatment.Transfer;
            default: warnings.Add($"{BadTreatment}:{raw}"); return RiskTreatment.Mitigate;
        }
    }

    private static RiskStatus ReadStatus(string? raw, List<string> warnings)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case null or "" or "open" or "new": return RiskStatus.Open;
            case "planned" or "in progress" or "ongoing" or "treating" or "in treatment": return RiskStatus.Treating;
            case "closed" or "done" or "completed" or "complete": return RiskStatus.Done;
            default: warnings.Add($"{BadStatus}:{raw}"); return RiskStatus.Open;
        }
    }

    /// <summary>"Q4 2024" is the quarter's last day; an Excel date number or a written date is taken as it is.</summary>
    private static DateOnly? ReadDate(string? raw, List<string> warnings)
    {
        if (raw == null) return null;
        var quarter = Quarter.Match(raw.Trim());
        if (quarter.Success)
        {
            var year = int.Parse(quarter.Groups[2].Value);
            var month = int.Parse(quarter.Groups[1].Value) * 3;
            return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        if (DateTime.TryParseExact(raw.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var written))
            return DateOnly.FromDateTime(written);
        warnings.Add($"{BadDate}:{raw}");
        return null;
    }
}
