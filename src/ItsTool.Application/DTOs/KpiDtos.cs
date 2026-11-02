namespace ItsTool.Application.DTOs;

/// <summary>
/// Which tickets a KPI report covers. From and To are dates in the service desk's time zone, both included;
/// without them the report covers last month. The other filters narrow the tickets: the requester's department,
/// the category, the priority, and a team or agent the ticket is assigned to.
/// </summary>
public record KpiFilterDto
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int? DepartmentId { get; init; }
    public int? CategoryId { get; init; }
    public int? PriorityId { get; init; }
    public int? GroupId { get; init; }
    public int? AgentUserId { get; init; }
}

/// <summary>
/// Service desk KPIs for a period (feature spec, "KPIs and reports for ISO audits"). Times are working minutes of the
/// business calendar. Rates are sent as counts (met and due) so the page can show "n of m" next to the percentage.
/// </summary>
public record KpiReportDto(
    DateOnly From,
    DateOnly To,
    DateTime GeneratedAt,
    KpiSummaryDto Summary,
    List<KpiMonthDto> Months,
    KpiBacklogDto Backlog,
    Dictionary<string, List<KpiBreakdownRowDto>> Breakdowns);

public record KpiSummaryDto(
    int Opened,
    int Solved,
    int Closed,
    int ResponseDue,
    int ResponseMet,
    int ResolutionDue,
    int ResolutionMet,
    double? MeanTimeToOwnMinutes,
    double? MeanTimeToResolveMinutes,
    double? MeanTimeToCloseMinutes,
    int Reopened,
    int FirstTimeFixed,
    int SurveyCount,
    double? SatisfactionAverage);

/// <summary>Tickets opened and solved in one calendar month of the period.</summary>
public record KpiMonthDto(int Year, int Month, int Opened, int Solved);

/// <summary>Tickets open now, whatever the period, by age in days; Late have passed their resolution deadline.</summary>
public record KpiBacklogDto(int Open, int Late, int Days0To3, int Days4To7, int Days8To30, int Over30Days);

/// <summary>One team, agent, category, department or priority. Id and Name are null for "none" (e.g. unassigned).</summary>
public record KpiBreakdownRowDto(
    int? Id,
    string? Name,
    int Opened,
    int Solved,
    double? MeanTimeToOwnMinutes,
    double? MeanTimeToResolveMinutes,
    int ResponseDue,
    int ResponseMet,
    int ResolutionDue,
    int ResolutionMet,
    int Reopened);

public record KpiOptionDto(int Id, string Name);

/// <summary>The values the KPI filters offer.</summary>
public record KpiFilterOptionsDto(
    List<KpiOptionDto> Departments,
    List<KpiOptionDto> Categories,
    List<KpiOptionDto> Priorities,
    List<KpiOptionDto> Teams,
    List<KpiOptionDto> Agents);
