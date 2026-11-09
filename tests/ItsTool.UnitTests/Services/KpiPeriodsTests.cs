using ItsTool.Domain.Entities.Kpis;

namespace ItsTool.UnitTests.Services;

public class KpiPeriodsTests
{
    [Theory]
    [InlineData(KpiCycle.Yearly, 1, "2026-01-01", "2026-12-31", "2026")]
    [InlineData(KpiCycle.HalfYearly, 2, "2026-07-01", "2026-12-31", "2026-H2")]
    [InlineData(KpiCycle.Quarterly, 3, "2026-07-01", "2026-09-30", "2026-Q3")]
    [InlineData(KpiCycle.Monthly, 2, "2024-02-01", "2024-02-29", "2024-02")]
    public void Periods_have_a_start_an_end_and_a_key(KpiCycle cycle, int period, string start, string end, string key)
    {
        var year = int.Parse(start[..4]);
        Assert.Equal(DateOnly.Parse(start), KpiPeriods.Start(cycle, year, period));
        Assert.Equal(DateOnly.Parse(end), KpiPeriods.End(cycle, year, period));
        Assert.Equal(key, KpiPeriods.Key(cycle, year, period));
        Assert.True(KpiPeriods.IsValid(cycle, period));
        Assert.False(KpiPeriods.IsValid(cycle, KpiPeriods.Count(cycle) + 1));
    }

    [Fact]
    public void The_last_completed_period_is_the_one_that_ended_before_today()
    {
        var today = new DateOnly(2026, 11, 9);
        Assert.Equal((2025, 1), KpiPeriods.LastCompleted(KpiCycle.Yearly, today));
        Assert.Equal((2026, 1), KpiPeriods.LastCompleted(KpiCycle.HalfYearly, today));
        Assert.Equal((2026, 3), KpiPeriods.LastCompleted(KpiCycle.Quarterly, today));
        Assert.Equal((2026, 10), KpiPeriods.LastCompleted(KpiCycle.Monthly, today));
        // The day a period ends it is not yet complete; the day after it is
        Assert.Equal((2026, 9), KpiPeriods.LastCompleted(KpiCycle.Monthly, new DateOnly(2026, 10, 31)));
        Assert.Equal((2026, 10), KpiPeriods.LastCompleted(KpiCycle.Monthly, new DateOnly(2026, 11, 1)));
        Assert.Equal((2025, 12), KpiPeriods.LastCompleted(KpiCycle.Monthly, new DateOnly(2026, 1, 15)));

        Assert.Equal(3, KpiPeriods.Completed(KpiCycle.Quarterly, 2026, today));
        Assert.Equal(4, KpiPeriods.Completed(KpiCycle.Quarterly, 2025, today));
        Assert.Equal(0, KpiPeriods.Completed(KpiCycle.Quarterly, 2027, today));
    }

    [Fact]
    public void The_year_figure_is_the_latest_value_or_the_average()
    {
        var values = new[] { (KpiCycle.Quarterly, 1, 5m), (KpiCycle.Quarterly, 3, 9m), (KpiCycle.Quarterly, 2, 7m) };
        Assert.Equal(9m, KpiPeriods.YearEnd(KpiYearEnd.Last, 2026, values));
        Assert.Equal(7m, KpiPeriods.YearEnd(KpiYearEnd.Average, 2026, values));
        Assert.Equal(96.35m, KpiPeriods.YearEnd(KpiYearEnd.Average, 2026, new[] { (KpiCycle.Monthly, 1, 96.27m), (KpiCycle.Monthly, 2, 96.42m) }));
        Assert.Null(KpiPeriods.YearEnd(KpiYearEnd.Last, 2026, Array.Empty<(KpiCycle, int, decimal)>()));
        // A yearly value entered before the cycle became quarterly counts as the year's end
        Assert.Equal(14m, KpiPeriods.YearEnd(KpiYearEnd.Last, 2026, new[] { (KpiCycle.Quarterly, 4, 12m), (KpiCycle.Yearly, 1, 14m) }));
    }
}
