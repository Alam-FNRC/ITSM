using System.Globalization;
using System.Text.RegularExpressions;
using ItsTool.Domain.Entities.Kpis;

namespace ItsTool.Infrastructure.Import;

/// <summary>
/// Reads FNRC's IT KPIs sheet: the sheet whose heading row has "اسم المؤشر" (KPI name), with columns found by their
/// Arabic headings and one column per year (a four-digit heading). The capability number and name are merged
/// across a capability's rows, so an empty cell carries the previous row's forward.
/// </summary>
public static class KpiWorkbook
{
    /// <summary>One KPI row with its yearly values. Warnings are "kind:value" so the page can word them in the reader's language.</summary>
    public sealed record Row(
        int RowNumber, int? Number, string NameAr, string? NameEn, int? CapabilityNumber, string? CapabilityNameAr,
        KpiResultType ResultType, KpiDirection Direction, KpiCycle Cycle,
        string? Formula, string? DataSource, string? Scope, string? Description, string? Unit,
        IReadOnlyDictionary<int, decimal> Values, IReadOnlyList<string> Warnings);

    public const string BadNumber = "number";
    public const string BadCycle = "cycle";
    public const string BadDirection = "direction";
    public const string Scaled = "scaled";
    public const string NoEnglish = "name";

    private static readonly Regex Year = new(@"^(20\d\d)$");
    private static readonly Regex LeadingNumber = new(@"-?\d+(?:[.,]\d+)?");

    private sealed class Columns
    {
        public int CapabilityNumber, CapabilityName, Number, Name, Result, Formula, Cycle, Direction, Source, Scope, Description, Unit;
        public readonly SortedDictionary<int, int> Years = new(); // year -> column
    }

    /// <exception cref="ArgumentException">Not a workbook, or no sheet with the KPI headings.</exception>
    public static IReadOnlyList<Row> Parse(Stream workbook)
    {
        foreach (var sheet in XlsxReader.Read(workbook))
        {
            foreach (var (rowNumber, cells) in sheet.Rows.OrderBy(r => r.Key))
            {
                if (!cells.Values.Any(v => Heading(v).Contains("اسم المؤشر"))) continue;
                var columns = FindColumns(cells);
                if (columns.Name == 0) continue;
                var rows = new List<Row>();
                int? capabilityNumber = null;
                string? capabilityName = null;
                foreach (var r in sheet.Rows.Keys.Where(r => r > rowNumber).OrderBy(r => r))
                {
                    var row = ReadRow(sheet, r, columns, ref capabilityNumber, ref capabilityName);
                    if (row != null) rows.Add(row);
                }
                return rows;
            }
        }
        throw new ArgumentException("No sheet in this workbook has the KPI sheet's headings (اسم المؤشر).");
    }

    private static string Heading(string text) => KpiNames.Normalize(text);

    private static Columns FindColumns(IReadOnlyDictionary<int, string> cells)
    {
        var columns = new Columns();
        foreach (var (column, raw) in cells.OrderBy(c => c.Key))
        {
            var h = Heading(raw);
            bool Take(ref int slot, bool matches) { if (slot != 0 || !matches) return false; slot = column; return true; }
            if (Year.IsMatch(h)) { columns.Years[int.Parse(h, CultureInfo.InvariantCulture)] = column; continue; }
            if (Take(ref columns.CapabilityName, h.Contains("اسم القدرة"))) continue;
            if (Take(ref columns.Name, h.Contains("اسم المؤشر"))) continue;
            if (Take(ref columns.CapabilityNumber, h == "القدرة")) continue;
            if (Take(ref columns.Number, h == "المؤشر")) continue;
            if (Take(ref columns.Result, h.Contains("طبيعة"))) continue;
            if (Take(ref columns.Formula, h.Contains("معادلة"))) continue;
            if (Take(ref columns.Cycle, h.Contains("دورة"))) continue;
            if (Take(ref columns.Direction, h.Contains("اتجاه"))) continue;
            if (Take(ref columns.Source, h.Contains("مصدر"))) continue;
            if (Take(ref columns.Scope, h.Contains("نطاق"))) continue;
            if (Take(ref columns.Description, h.Contains("وصف"))) continue;
            Take(ref columns.Unit, h.Contains("وحدة"));
        }
        return columns;
    }

    private static Row? ReadRow(XlsxReader.Sheet sheet, int row, Columns columns, ref int? capabilityNumber, ref string? capabilityName)
    {
        string? Text(int column) => column == 0 ? null : sheet.Cell(row, column) is { Length: > 0 } value ? KpiNames.Normalize(value) : null;

        // A capability's number and name sit on its first KPI row only
        if (WholeNumber(Text(columns.CapabilityNumber)) is { } cap) capabilityNumber = cap;
        if (Text(columns.CapabilityName) is { } capName) capabilityName = capName;

        var name = Text(columns.Name);
        if (name == null) return null;
        var warnings = new List<string>();

        var result = (Text(columns.Result) ?? string.Empty).Contains("نسبة") ? KpiResultType.Percentage : KpiResultType.Number;
        var values = new Dictionary<int, decimal>();
        foreach (var (year, column) in columns.Years)
        {
            var raw = Text(column);
            if (raw == null) continue;
            var match = LeadingNumber.Match(raw);
            if (!match.Success || !decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                warnings.Add($"{BadNumber}:{raw}");
                continue;
            }
            if (match.Value != raw) warnings.Add($"{BadNumber}:{raw}");
            // The sheet keeps percentages as fractions (0.99 for 99%)
            if (result == KpiResultType.Percentage && number <= 1)
            {
                number *= 100;
                warnings.Add($"{Scaled}:{raw}");
            }
            values[year] = Math.Round(number, 2);
        }

        var english = KpiNames.Of(name);
        if (english == null) warnings.Add($"{NoEnglish}:");

        return new Row(
            row, WholeNumber(Text(columns.Number)), name, english, capabilityNumber, capabilityName,
            result, ReadDirection(Text(columns.Direction), warnings), ReadCycle(Text(columns.Cycle), warnings),
            Text(columns.Formula), Text(columns.Source), Translate(Text(columns.Scope)), Text(columns.Description), Translate(Text(columns.Unit)),
            values, warnings);
    }

    private static string? Translate(string? arabic) => arabic == null ? null : KpiNames.Of(arabic) ?? arabic;

    private static int? WholeNumber(string? raw) =>
        raw != null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value == Math.Floor(value) && value is > 0 and < 1000
            ? (int)value : null;

    private static KpiDirection ReadDirection(string? raw, List<string> warnings)
    {
        if (raw == null) return KpiDirection.Up;
        if (raw.Contains("تصاعد") || raw.Contains('▲')) return KpiDirection.Up;
        if (raw.Contains("تنازل") || raw.Contains('▼')) return KpiDirection.Down;
        if (raw.Contains("رصد") || raw.Contains('◆')) return KpiDirection.Monitor;
        warnings.Add($"{BadDirection}:{raw}");
        return KpiDirection.Up;
    }

    /// <summary>"At each release" and anything else unknown is read as yearly, with a warning; the administrator sets the cycle.</summary>
    private static KpiCycle ReadCycle(string? raw, List<string> warnings)
    {
        if (raw == null) return KpiCycle.Yearly;
        if (raw.Contains("نصف")) return KpiCycle.HalfYearly;
        if (raw.Contains("ربع")) return KpiCycle.Quarterly;
        if (raw.Contains("شهري")) return KpiCycle.Monthly;
        if (raw.Contains("سنوي")) return KpiCycle.Yearly;
        warnings.Add($"{BadCycle}:{raw}");
        return KpiCycle.Yearly;
    }
}
