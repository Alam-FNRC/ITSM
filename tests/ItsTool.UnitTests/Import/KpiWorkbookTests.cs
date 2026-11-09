using ItsTool.Domain.Entities.Kpis;
using ItsTool.Infrastructure.Import;
using ItsTool.Tests.Shared;

namespace ItsTool.UnitTests.Import;

public class KpiWorkbookTests
{
    private static MemoryStream Sheet(params IEnumerable<(string, string)>[] rows) =>
        TestWorkbook.Build(TestWorkbook.KpiHeadings.Concat(rows.SelectMany(r => r)), "Sheet1");

    [Fact]
    public void Rows_are_read_by_their_arabic_headings_and_the_capability_carries_forward()
    {
        using var file = Sheet(
            TestWorkbook.KpiRow(4, "1", "استراتيجية الذكاء الاصطناعي والإبتكار الرقمي", "1", "عدد اللوحات التحليلية والتنبؤية", unit: "لوحة", scope: "استراتيجي", v2025: "14", v2026: "14"),
            TestWorkbook.KpiRow(5, "", "", "2", "عدد الحلول المدعومة بالذكاء الاصطناعي المطبقة", unit: "حل", v2025: "3", v2026: "6"),
            TestWorkbook.KpiRow(6, "2", "خطة التحول الرقمي", "3", "نسبة الخدمات الخارجية المرقمنة", result: "نسبة مئوية", v2023: "11", v2026: "14"));

        var rows = KpiWorkbook.Parse(file);

        Assert.Equal(3, rows.Count);
        Assert.Equal((1, "Analytical and predictive dashboards", 1, "استراتيجية الذكاء الاصطناعي والإبتكار الرقمي"), (rows[0].Number, rows[0].NameEn, rows[0].CapabilityNumber, rows[0].CapabilityNameAr));
        Assert.Equal((KpiResultType.Number, KpiDirection.Up, KpiCycle.Yearly, "dashboard", "Strategic"), (rows[0].ResultType, rows[0].Direction, rows[0].Cycle, rows[0].Unit, rows[0].Scope));
        Assert.Equal(new Dictionary<int, decimal> { [2025] = 14, [2026] = 14 }, rows[0].Values);
        Assert.Empty(rows[0].Warnings);

        // The merged capability cells are empty on the second row
        Assert.Equal((1, "استراتيجية الذكاء الاصطناعي والإبتكار الرقمي", "solution"), (rows[1].CapabilityNumber, rows[1].CapabilityNameAr, rows[1].Unit));
        Assert.Equal((2, KpiResultType.Percentage, "Digitised external services"), (rows[2].CapabilityNumber, rows[2].ResultType, rows[2].NameEn));
        Assert.Equal(new Dictionary<int, decimal> { [2023] = 11, [2026] = 14 }, rows[2].Values);
    }

    [Fact]
    public void Fractions_text_and_unknown_words_become_warnings_instead_of_guesses()
    {
        using var file = Sheet(
            TestWorkbook.KpiRow(7, "3", "إدارة القنوات الرقمية", "4", "نسبة إتاحة الموقع الإلكتروني والتطبيق الذكي", result: "نسبة مئوية", unit: "وحدة مئوية", v2026: "0.99"),
            TestWorkbook.KpiRow(8, "", "", "5", "زمن دورة تطوير الموقع الإلكتروني", cycle: "عند كل إصدار", direction: "تنازلي ▼", unit: "سنة", scope: "الموقع الإلكتروني", v2025: "6 سنوات", v2026: "غير متاح"),
            TestWorkbook.KpiRow(9, "", "", "", "مؤشر جديد", direction: "جانبي"));

        var rows = KpiWorkbook.Parse(file);

        Assert.Equal(new Dictionary<int, decimal> { [2026] = 99 }, rows[0].Values);
        Assert.Equal(new[] { "scaled:0.99" }, rows[0].Warnings);
        Assert.Equal("%", rows[0].Unit);

        Assert.Equal((KpiCycle.Yearly, KpiDirection.Down, "year", "Website"), (rows[1].Cycle, rows[1].Direction, rows[1].Unit, rows[1].Scope));
        Assert.Equal(new Dictionary<int, decimal> { [2025] = 6 }, rows[1].Values);
        Assert.Equal(new[] { "number:6 سنوات", "number:غير متاح", "cycle:عند كل إصدار" }, rows[1].Warnings);

        Assert.Null(rows[2].Number);
        Assert.Null(rows[2].NameEn);
        Assert.Equal(new[] { "name:", "direction:جانبي" }, rows[2].Warnings);
    }

    [Fact]
    public void A_workbook_without_the_kpi_headings_is_refused()
    {
        using var risks = TestWorkbook.Build(TestWorkbook.RegisterHeadings);
        var error = Assert.Throws<ArgumentException>(() => KpiWorkbook.Parse(risks));
        Assert.Contains("اسم المؤشر", error.Message);
    }
}
