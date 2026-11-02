using ItsTool.Application.DTOs;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// Works out the service desk KPIs for a period from one row per ticket (feature spec, "KPIs and reports for ISO
/// audits"). Kept apart from the database so the definitions can be tested on their own:
/// <list type="bullet">
/// <item>Opened, solved and closed count the tickets whose opening, last solution or closing falls in the period.</item>
/// <item>Mean times use the stored ticket times (working minutes) of the tickets owned, solved or closed in the period.</item>
/// <item>SLA targets are judged for the tickets opened in the period, once the outcome is known: met, missed, or
/// still open past the deadline. A ticket that is open and not yet due is left out.</item>
/// <item>Reopened and first-time fix look at the tickets solved in the period; a first-time fix was never reopened
/// and never passed to another team or agent.</item>
/// <item>The backlog is the tickets open now, whatever the period.</item>
/// </list>
/// </summary>
public static class KpiCalculator
{
    public const string ByService = "service";
    public const string ByTeam = "team";
    public const string ByAgent = "agent";
    public const string ByCategory = "category";
    public const string ByDepartment = "department";
    public const string ByPriority = "priority";

    /// <summary>The longest period one report covers, so a request cannot load the whole ticket history.</summary>
    public const int MaxDays = 731;

    public sealed record TicketRow(
        int Id,
        DateTime CreatedAt,
        bool IsOpen,
        int? DepartmentId,
        int CategoryId,
        int PriorityId,
        IReadOnlyList<int> GroupIds,
        IReadOnlyList<int> AgentIds,
        DateTime? OwnedAt = null,
        int? TimeToOwnMinutes = null,
        DateTime? ResolvedAt = null,
        int? TimeToResolveMinutes = null,
        DateTime? ClosedAt = null,
        int? TimeToCloseMinutes = null,
        int ReopenCount = 0,
        int AssignmentCount = 0,
        DateTime? ResponseDueAt = null,
        DateTime? ResponseMetAt = null,
        bool ResponseBreached = false,
        DateTime? ResolutionDueAt = null,
        bool ResolutionBreached = false,
        bool SlaPaused = false,
        int? ServiceId = null);

    /// <summary>A period of whole local days: FromUtc is the start of From, ToUtc the start of the day after To.</summary>
    public sealed record Period(DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtc, TimeZoneInfo Zone)
    {
        public bool Contains(DateTime? utc) => utc.HasValue && utc.Value >= FromUtc && utc.Value < ToUtc;
    }

    /// <summary>Names for the breakdown rows; an id with no name (e.g. a removed group) shows as its number.</summary>
    public sealed record Names(
        IReadOnlyDictionary<int, string> Teams,
        IReadOnlyDictionary<int, string> Agents,
        IReadOnlyDictionary<int, string> Categories,
        IReadOnlyDictionary<int, string> Departments,
        IReadOnlyDictionary<int, string> Priorities,
        IReadOnlyDictionary<int, string> Services)
    {
        public static readonly Names None = new(
            new Dictionary<int, string>(), new Dictionary<int, string>(), new Dictionary<int, string>(),
            new Dictionary<int, string>(), new Dictionary<int, string>(), new Dictionary<int, string>());
    }

    /// <summary>
    /// The period asked for, or last month. With only From it runs to today; with only To it starts on the first of
    /// To's month.
    /// </summary>
    /// <exception cref="ArgumentException">From is after To, or the period is longer than <see cref="MaxDays"/>.</exception>
    public static Period ResolvePeriod(DateOnly? from, DateOnly? to, DateTime nowUtc, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
        DateOnly start, end;
        if (from == null && to == null)
        {
            var thisMonth = new DateOnly(today.Year, today.Month, 1);
            start = thisMonth.AddMonths(-1);
            end = thisMonth.AddDays(-1);
        }
        else
        {
            end = to ?? today;
            start = from ?? new DateOnly(end.Year, end.Month, 1);
        }

        if (start > end) throw new ArgumentException("The start date must not be after the end date.");
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) throw new ArgumentException($"A report covers at most {MaxDays} days.");

        return new Period(start, end, StartOfDayUtc(start, zone), StartOfDayUtc(end.AddDays(1), zone), zone);
    }

    private static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);

    public static KpiReportDto Calculate(
        IReadOnlyCollection<TicketRow> tickets, IReadOnlyCollection<int> surveyRatings, Period period, DateTime nowUtc, Names names)
    {
        var all = new Tally();
        foreach (var ticket in tickets) all.Add(ticket, period, nowUtc);

        var summary = new KpiSummaryDto(
            all.Opened, all.Solved, all.Closed,
            all.ResponseDue, all.ResponseMet, all.ResolutionDue, all.ResolutionMet,
            all.Own.Value, all.Resolve.Value, all.Close.Value,
            all.Reopened, all.FirstTimeFixed,
            surveyRatings.Count, surveyRatings.Count > 0 ? Math.Round(surveyRatings.Average(), 2) : null);

        return new KpiReportDto(
            period.From, period.To, nowUtc, summary,
            Months(tickets, period),
            Backlog(tickets, nowUtc),
            new Dictionary<string, List<KpiBreakdownRowDto>>
            {
                [ByService] = Breakdown(tickets, t => new[] { t.ServiceId }, names.Services, period, nowUtc),
                [ByTeam] = Breakdown(tickets, t => Ids(t.GroupIds), names.Teams, period, nowUtc),
                [ByAgent] = Breakdown(tickets, t => Ids(t.AgentIds), names.Agents, period, nowUtc),
                [ByCategory] = Breakdown(tickets, t => new int?[] { t.CategoryId }, names.Categories, period, nowUtc),
                [ByDepartment] = Breakdown(tickets, t => new[] { t.DepartmentId }, names.Departments, period, nowUtc),
                [ByPriority] = Breakdown(tickets, t => new int?[] { t.PriorityId }, names.Priorities, period, nowUtc)
            });
    }

    /// <summary>Whether the response target was met: null while it cannot be judged yet, or without a target.</summary>
    public static bool? ResponseOutcome(TicketRow t, DateTime nowUtc)
    {
        if (!t.ResponseDueAt.HasValue) return null;
        if (t.ResponseBreached) return false;
        if (t.ResponseMetAt.HasValue) return t.ResponseMetAt.Value <= t.ResponseDueAt.Value;
        return t.ResponseDueAt.Value <= nowUtc && !t.SlaPaused ? false : null;
    }

    /// <summary>Whether the resolution target was met, judged on the last solution if the ticket was reopened.</summary>
    public static bool? ResolutionOutcome(TicketRow t, DateTime nowUtc)
    {
        if (!t.ResolutionDueAt.HasValue) return null;
        if (t.ResolutionBreached) return false;
        if (!t.IsOpen && t.ResolvedAt.HasValue) return t.ResolvedAt.Value <= t.ResolutionDueAt.Value;
        return t.ResolutionDueAt.Value <= nowUtc && !t.SlaPaused ? false : null;
    }

    // A ticket counts once for each team or agent it is assigned to; with none it counts under "none"
    private static IEnumerable<int?> Ids(IReadOnlyList<int> ids) => ids.Count == 0 ? new int?[] { null } : ids.Distinct().Select(i => (int?)i);

    private static List<KpiBreakdownRowDto> Breakdown(
        IEnumerable<TicketRow> tickets, Func<TicketRow, IEnumerable<int?>> keys, IReadOnlyDictionary<int, string> names, Period period, DateTime nowUtc)
    {
        var tallies = new Dictionary<int, Tally>();
        var none = new Tally();
        foreach (var ticket in tickets)
        {
            foreach (var key in keys(ticket))
            {
                var tally = key.HasValue ? (tallies.TryGetValue(key.Value, out var found) ? found : tallies[key.Value] = new Tally()) : none;
                tally.Add(ticket, period, nowUtc);
            }
        }

        return tallies.Select(kv => Row(kv.Key, names.TryGetValue(kv.Key, out var name) ? name : $"#{kv.Key}", kv.Value))
            .Append(Row(null, null, none))
            .Where(r => r.Opened + r.Solved + r.ResponseDue + r.ResolutionDue > 0 || r.MeanTimeToOwnMinutes.HasValue)
            .OrderByDescending(r => r.Opened).ThenByDescending(r => r.Solved).ThenBy(r => r.Name == null).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static KpiBreakdownRowDto Row(int? id, string? name, Tally t) =>
        new(id, name, t.Opened, t.Solved, t.Own.Value, t.Resolve.Value, t.ResponseDue, t.ResponseMet, t.ResolutionDue, t.ResolutionMet, t.Reopened);

    private static List<KpiMonthDto> Months(IEnumerable<TicketRow> tickets, Period period)
    {
        var months = new List<KpiMonthDto>();
        var counts = new Dictionary<(int, int), (int Opened, int Solved)>();
        (int, int) MonthOf(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, period.Zone);
            return (local.Year, local.Month);
        }

        foreach (var t in tickets)
        {
            if (period.Contains(t.CreatedAt))
            {
                var m = MonthOf(t.CreatedAt);
                counts[m] = counts.TryGetValue(m, out var c) ? (c.Opened + 1, c.Solved) : (1, 0);
            }
            if (!t.IsOpen && period.Contains(t.ResolvedAt))
            {
                var m = MonthOf(t.ResolvedAt!.Value);
                counts[m] = counts.TryGetValue(m, out var c) ? (c.Opened, c.Solved + 1) : (0, 1);
            }
        }

        for (var month = new DateOnly(period.From.Year, period.From.Month, 1); month <= period.To; month = month.AddMonths(1))
        {
            counts.TryGetValue((month.Year, month.Month), out var c);
            months.Add(new KpiMonthDto(month.Year, month.Month, c.Opened, c.Solved));
        }
        return months;
    }

    private static KpiBacklogDto Backlog(IEnumerable<TicketRow> tickets, DateTime nowUtc)
    {
        int open = 0, late = 0, upTo3 = 0, upTo7 = 0, upTo30 = 0, older = 0;
        foreach (var t in tickets.Where(t => t.IsOpen))
        {
            open++;
            if (ResolutionOutcome(t, nowUtc) == false) late++;
            var days = (int)Math.Floor((nowUtc - t.CreatedAt).TotalDays);
            if (days <= 3) upTo3++;
            else if (days <= 7) upTo7++;
            else if (days <= 30) upTo30++;
            else older++;
        }
        return new KpiBacklogDto(open, late, upTo3, upTo7, upTo30, older);
    }

    private sealed class Average
    {
        private long _sum;
        private int _count;

        public void Add(int? minutes)
        {
            if (!minutes.HasValue) return;
            _sum += minutes.Value;
            _count++;
        }

        public double? Value => _count == 0 ? null : Math.Round((double)_sum / _count, 1);
    }

    private sealed class Tally
    {
        public int Opened, Solved, Closed, ResponseDue, ResponseMet, ResolutionDue, ResolutionMet, Reopened, FirstTimeFixed;
        public readonly Average Own = new(), Resolve = new(), Close = new();

        public void Add(TicketRow t, Period period, DateTime nowUtc)
        {
            if (period.Contains(t.CreatedAt))
            {
                Opened++;
                Judge(ResponseOutcome(t, nowUtc), ref ResponseDue, ref ResponseMet);
                Judge(ResolutionOutcome(t, nowUtc), ref ResolutionDue, ref ResolutionMet);
            }
            if (period.Contains(t.OwnedAt)) Own.Add(t.TimeToOwnMinutes);
            if (!t.IsOpen && period.Contains(t.ResolvedAt))
            {
                Solved++;
                Resolve.Add(t.TimeToResolveMinutes);
                if (t.ReopenCount > 0) Reopened++;
                else if (t.AssignmentCount <= 1) FirstTimeFixed++;
            }
            if (!t.IsOpen && period.Contains(t.ClosedAt))
            {
                Closed++;
                Close.Add(t.TimeToCloseMinutes);
            }
        }

        private static void Judge(bool? met, ref int due, ref int metCount)
        {
            if (met == null) return;
            due++;
            if (met.Value) metCount++;
        }
    }
}
