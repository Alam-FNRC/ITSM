using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Kpis;

/// <summary>How often the owner enters a value (decided 9 Nov 2026); set by an administrator, never by the owner.</summary>
public enum KpiCycle
{
    Yearly = 0,
    HalfYearly = 1,
    Quarterly = 2,
    Monthly = 3
}

public enum KpiResultType
{
    Number = 0,
    Percentage = 1
}

/// <summary>Up: higher is better. Down: lower is better. Monitor: watched, with no better or worse.</summary>
public enum KpiDirection
{
    Up = 0,
    Down = 1,
    Monitor = 2
}

/// <summary>How the year's figure is formed from the periods entered: the last value (counts) or their average (rates, times).</summary>
public enum KpiYearEnd
{
    Last = 0,
    Average = 1
}

/// <summary>Where a value comes from: typed by the owner, or worked out from this system's tickets (the owner may still override).</summary>
public enum KpiSource
{
    Manual = 0,
    TicketResolutionTime = 1
}

/// <summary>A capability (القدرة) of the IT KPIs sheet, grouping its KPIs.</summary>
public class KpiCapability : BaseEntity
{
    public int Number { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;

    public virtual ICollection<KpiDefinition> Kpis { get; set; } = new List<KpiDefinition>();
}

/// <summary>
/// One IT KPI (feature spec, "IT KPI register"), replacing a row of the IT KPIs sheet. kpi.manage keeps the
/// definitions, owners, cycles and targets; the owner enters the values. Changes go to the system audit log.
/// </summary>
public class KpiDefinition : BaseEntity
{
    /// <summary>The sheet's KPI number; unique among the KPIs that exist.</summary>
    public int Number { get; set; }
    public int CapabilityId { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Formula { get; set; }
    public string? DataSource { get; set; }
    public string? Scope { get; set; }
    public string? Unit { get; set; }
    public KpiResultType ResultType { get; set; }
    public KpiDirection Direction { get; set; }
    public KpiCycle Cycle { get; set; }
    public KpiYearEnd YearEnd { get; set; }
    public KpiSource Source { get; set; }
    public int? OwnerUserId { get; set; }

    public virtual KpiCapability? Capability { get; set; }
    public virtual ICollection<KpiValue> Values { get; set; } = new List<KpiValue>();
    public virtual ICollection<KpiTarget> Targets { get; set; } = new List<KpiTarget>();
}

/// <summary>A value entered for one period. The cycle is kept with it, so values stay meaningful if the cycle changes later.</summary>
public class KpiValue : BaseEntity
{
    public int KpiId { get; set; }
    public int Year { get; set; }
    public KpiCycle Cycle { get; set; }
    /// <summary>1 for a year, 1-2 for half-years, 1-4 for quarters, 1-12 for months (see KpiPeriods).</summary>
    public int Period { get; set; }
    public decimal Value { get; set; }
    public string? Note { get; set; }
    public int EnteredByUserId { get; set; }

    public virtual KpiDefinition? Kpi { get; set; }
}

/// <summary>The target for one year, optional.</summary>
public class KpiTarget : BaseEntity
{
    public int KpiId { get; set; }
    public int Year { get; set; }
    public decimal Value { get; set; }

    public virtual KpiDefinition? Kpi { get; set; }
}
