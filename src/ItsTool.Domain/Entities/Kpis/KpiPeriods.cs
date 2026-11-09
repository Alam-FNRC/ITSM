namespace ItsTool.Domain.Entities.Kpis;

/// <summary>The periods of a measurement cycle, and how a year's figure is formed from them. Mirrored in js/kpi-register.js.</summary>
public static class KpiPeriods
{
    public const int FirstYear = 2000;
    public const int LastYear = 2100;

    public static int Count(KpiCycle cycle) => cycle switch
    {
        KpiCycle.Monthly => 12,
        KpiCycle.Quarterly => 4,
        KpiCycle.HalfYearly => 2,
        _ => 1
    };

    public static bool IsValid(KpiCycle cycle, int period) => period >= 1 && period <= Count(cycle);

    public static bool IsYear(int year) => year is >= FirstYear and <= LastYear;

    public static DateOnly Start(KpiCycle cycle, int year, int period) => new(year, (period - 1) * (12 / Count(cycle)) + 1, 1);

    public static DateOnly End(KpiCycle cycle, int year, int period)
    {
        var month = period * (12 / Count(cycle));
        return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
    }

    /// <summary>"2026", "2026-H1", "2026-Q3" or "2026-07": how reminders and notes name a period.</summary>
    public static string Key(KpiCycle cycle, int year, int period) => cycle switch
    {
        KpiCycle.Monthly => $"{year}-{period:D2}",
        KpiCycle.Quarterly => $"{year}-Q{period}",
        KpiCycle.HalfYearly => $"{year}-H{period}",
        _ => year.ToString()
    };

    /// <summary>How many of the year's periods have ended by today.</summary>
    public static int Completed(KpiCycle cycle, int year, DateOnly today) =>
        Enumerable.Range(1, Count(cycle)).Count(p => End(cycle, year, p) < today);

    /// <summary>The most recent period that has ended, or null before the first one ever (the year 2000).</summary>
    public static (int Year, int Period)? LastCompleted(KpiCycle cycle, DateOnly today)
    {
        for (var period = Count(cycle); period >= 1; period--)
        {
            if (End(cycle, today.Year, period) < today) return (today.Year, period);
        }
        return today.Year > FirstYear ? (today.Year - 1, Count(cycle)) : null;
    }

    /// <summary>The year's figure: the latest period's value, or the average of the periods entered, rounded to 2 decimals.</summary>
    public static decimal? YearEnd(KpiYearEnd rule, int year, IReadOnlyCollection<(KpiCycle Cycle, int Period, decimal Value)> values)
    {
        if (values.Count == 0) return null;
        return rule == KpiYearEnd.Average
            ? Math.Round(values.Average(v => v.Value), 2)
            : values.OrderByDescending(v => End(v.Cycle, year, v.Period)).ThenBy(v => Count(v.Cycle)).First().Value;
    }
}
