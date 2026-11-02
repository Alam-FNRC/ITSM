using ItsTool.Infrastructure.Services;
using static ItsTool.Infrastructure.Services.KpiCalculator;

namespace ItsTool.UnitTests.Services;

public class KpiCalculatorTests
{
    private static readonly TimeZoneInfo Uae = BusinessCalendar.ResolveTimeZone(null);
    // Monday 2 November 2026, 10:00 UAE time
    private static readonly DateTime Now = Utc(11, 2, 6);
    // October 2026 in UAE time
    private static readonly Period October = ResolvePeriod(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Now, Uae);

    private static DateTime Utc(int month, int day, int hour = 8) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static TicketRow Ticket(DateTime created, bool open = false, int category = 1, int priority = 3, int? department = null,
        int[]? groups = null, int[]? agents = null) =>
        new(Id: 0, CreatedAt: created, IsOpen: open, DepartmentId: department, CategoryId: category, PriorityId: priority,
            GroupIds: groups ?? Array.Empty<int>(), AgentIds: agents ?? Array.Empty<int>());

    private static ItsTool.Application.DTOs.KpiReportDto Calculate(IReadOnlyCollection<TicketRow> tickets, params int[] ratings) =>
        KpiCalculator.Calculate(tickets, ratings, October, Now, Names.None);

    [Fact]
    public void Without_dates_the_report_covers_last_month_in_uae_time()
    {
        var period = ResolvePeriod(null, null, Now, Uae);

        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (period.From, period.To));
        Assert.Equal(new DateTime(2026, 9, 30, 20, 0, 0), period.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 31, 20, 0, 0), period.ToUtc);
    }

    [Fact]
    public void A_missing_end_is_today_and_a_missing_start_is_the_first_of_the_month()
    {
        Assert.Equal(new DateOnly(2026, 11, 2), ResolvePeriod(new DateOnly(2026, 10, 15), null, Now, Uae).To);
        Assert.Equal(new DateOnly(2026, 9, 1), ResolvePeriod(null, new DateOnly(2026, 9, 20), Now, Uae).From);
    }

    [Fact]
    public void A_period_the_wrong_way_round_or_over_two_years_is_refused()
    {
        Assert.Throws<ArgumentException>(() => ResolvePeriod(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1), Now, Uae));
        Assert.Throws<ArgumentException>(() => ResolvePeriod(new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), Now, Uae));
        Assert.Equal(MaxDays, ResolvePeriod(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31), Now, Uae).To.DayNumber - new DateOnly(2024, 1, 1).DayNumber + 1);
    }

    [Fact]
    public void Tickets_count_by_when_they_were_opened_solved_and_closed()
    {
        var report = Calculate(new[]
        {
            Ticket(Utc(10, 5)) with { ResolvedAt = Utc(10, 6), ClosedAt = Utc(10, 8) },
            Ticket(Utc(9, 20)) with { ResolvedAt = Utc(10, 2) },                        // opened before, solved in October
            Ticket(Utc(10, 30), open: true),                                            // opened, still open
            Ticket(Utc(10, 25)) with { ResolvedAt = Utc(11, 1), ClosedAt = Utc(11, 1) }, // solved after October
            Ticket(Utc(9, 1)) with { ResolvedAt = Utc(9, 2) }                           // nothing in October
        });

        Assert.Equal((3, 2, 1), (report.Summary.Opened, report.Summary.Solved, report.Summary.Closed));
    }

    [Fact]
    public void The_uae_day_decides_the_month()
    {
        // 31 October 23:00 UAE is still October; 1 November 01:00 UAE is not, although both are 31 October in UTC
        var report = Calculate(new[] { Ticket(new DateTime(2026, 10, 31, 19, 0, 0, DateTimeKind.Utc)), Ticket(new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc)) });

        Assert.Equal(1, report.Summary.Opened);
        Assert.Equal(new[] { (2026, 10, 1, 0) }, report.Months.Select(m => (m.Year, m.Month, m.Opened, m.Solved)));
    }

    [Fact]
    public void Each_month_of_the_period_is_listed_even_when_empty()
    {
        var period = ResolvePeriod(new DateOnly(2026, 8, 15), new DateOnly(2026, 10, 31), Now, Uae);
        var report = KpiCalculator.Calculate(new[] { Ticket(Utc(10, 5)) with { ResolvedAt = Utc(10, 6) }, Ticket(Utc(8, 20), open: true) },
            Array.Empty<int>(), period, Now, Names.None);

        Assert.Equal(new[] { (8, 1, 0), (9, 0, 0), (10, 1, 1) }, report.Months.Select(m => (m.Month, m.Opened, m.Solved)));
    }

    [Fact]
    public void Mean_times_use_the_tickets_owned_solved_and_closed_in_the_period()
    {
        var report = Calculate(new[]
        {
            Ticket(Utc(10, 5)) with { OwnedAt = Utc(10, 5), TimeToOwnMinutes = 30, ResolvedAt = Utc(10, 6), TimeToResolveMinutes = 300, ClosedAt = Utc(10, 7), TimeToCloseMinutes = 700 },
            Ticket(Utc(10, 9)) with { OwnedAt = Utc(10, 9), TimeToOwnMinutes = 45, ResolvedAt = Utc(10, 12), TimeToResolveMinutes = 101 },
            Ticket(Utc(9, 1)) with { OwnedAt = Utc(9, 1), TimeToOwnMinutes = 9999, ResolvedAt = Utc(9, 2), TimeToResolveMinutes = 9999 }
        });

        Assert.Equal(37.5, report.Summary.MeanTimeToOwnMinutes);
        Assert.Equal(200.5, report.Summary.MeanTimeToResolveMinutes);
        Assert.Equal(700, report.Summary.MeanTimeToCloseMinutes);
    }

    [Fact]
    public void With_nothing_solved_there_is_no_mean_time()
    {
        var report = Calculate(new[] { Ticket(Utc(10, 5), open: true) });

        Assert.Null(report.Summary.MeanTimeToResolveMinutes);
        Assert.Null(report.Summary.SatisfactionAverage);
    }

    [Fact]
    public void Response_targets_are_judged_once_the_outcome_is_known()
    {
        var due = Utc(10, 5, 12);
        var report = Calculate(new[]
        {
            Ticket(Utc(10, 5)) with { ResponseDueAt = due, ResponseMetAt = Utc(10, 5, 9) },             // met
            Ticket(Utc(10, 5)) with { ResponseDueAt = due, ResponseMetAt = Utc(10, 5, 13) },            // answered late
            Ticket(Utc(10, 5)) with { ResponseDueAt = due, ResponseBreached = true },                   // flagged by the SLA job
            Ticket(Utc(10, 5), open: true) with { ResponseDueAt = due },                                // no answer, deadline passed
            Ticket(Utc(10, 31), open: true) with { ResponseDueAt = Utc(11, 3) },                        // not due yet
            Ticket(Utc(10, 5), open: true) with { ResponseDueAt = due, SlaPaused = true },              // on hold: the deadline moves
            Ticket(Utc(10, 5))                                                                          // no SLA
        });

        Assert.Equal((4, 1), (report.Summary.ResponseDue, report.Summary.ResponseMet));
    }

    [Fact]
    public void Resolution_targets_use_the_last_solution()
    {
        var due = Utc(10, 10);
        var report = Calculate(new[]
        {
            Ticket(Utc(10, 5)) with { ResolutionDueAt = due, ResolvedAt = Utc(10, 9) },                       // met
            Ticket(Utc(10, 5)) with { ResolutionDueAt = due, ResolvedAt = Utc(10, 12), ReopenCount = 1 },     // solved again after the deadline
            Ticket(Utc(10, 5), open: true) with { ResolutionDueAt = due },                                    // still open, late
            Ticket(Utc(10, 30), open: true) with { ResolutionDueAt = Utc(11, 10) }                            // still open, not due
        });

        Assert.Equal((3, 1), (report.Summary.ResolutionDue, report.Summary.ResolutionMet));
    }

    [Fact]
    public void A_first_time_fix_was_never_reopened_or_passed_on()
    {
        var report = Calculate(new[]
        {
            Ticket(Utc(10, 5)) with { ResolvedAt = Utc(10, 6), AssignmentCount = 1 },
            Ticket(Utc(10, 5)) with { ResolvedAt = Utc(10, 6), AssignmentCount = 0 },
            Ticket(Utc(10, 5)) with { ResolvedAt = Utc(10, 6), AssignmentCount = 2 },
            Ticket(Utc(10, 5)) with { ResolvedAt = Utc(10, 6), AssignmentCount = 1, ReopenCount = 1 }
        });

        Assert.Equal((4, 2, 1), (report.Summary.Solved, report.Summary.FirstTimeFixed, report.Summary.Reopened));
    }

    [Fact]
    public void The_backlog_is_what_is_open_now_by_age()
    {
        var report = Calculate(new[]
        {
            Ticket(Now.AddDays(-1), open: true),
            Ticket(Now.AddDays(-3.5), open: true),
            Ticket(Now.AddDays(-5), open: true) with { ResolutionDueAt = Now.AddDays(-1) },
            Ticket(Now.AddDays(-30), open: true),
            Ticket(Now.AddDays(-31), open: true) with { ResolutionDueAt = Now.AddDays(1), ResolutionBreached = true }, // deadline moved by a pause, but breached before
            Ticket(Now.AddDays(-40)) with { ResolvedAt = Now.AddDays(-39) }
        });

        Assert.Equal(new ItsTool.Application.DTOs.KpiBacklogDto(5, 2, 2, 1, 1, 1), report.Backlog);
    }

    [Fact]
    public void A_ticket_counts_for_every_team_it_is_assigned_to_and_unassigned_tickets_are_listed_apart()
    {
        var names = Names.None with { Teams = new Dictionary<int, string> { [7] = "Network", [8] = "Desktop" } };
        var report = KpiCalculator.Calculate(new[]
        {
            Ticket(Utc(10, 5), groups: new[] { 7, 8 }) with { ResolvedAt = Utc(10, 6), TimeToResolveMinutes = 60 },
            Ticket(Utc(10, 6), groups: new[] { 7 }),
            Ticket(Utc(10, 7), groups: new[] { 9 }),
            Ticket(Utc(10, 8))
        }, Array.Empty<int>(), October, Now, names);

        var teams = report.Breakdowns[ByTeam];
        Assert.Equal(new[] { ("Network", 2, 1), ("Desktop", 1, 1), ("#9", 1, 0), (null, 1, 0) },
            teams.Select(r => (r.Name, r.Opened, r.Solved)));
        Assert.Equal(60, teams[0].MeanTimeToResolveMinutes);
        Assert.Null(teams[^1].Id);
    }

    [Fact]
    public void Tickets_are_broken_down_by_the_service_of_their_category()
    {
        var names = Names.None with { Services = new Dictionary<int, string> { [3] = "Email" } };
        var report = KpiCalculator.Calculate(new[]
        {
            Ticket(Utc(10, 5)) with { ServiceId = 3, ResolvedAt = Utc(10, 6) },
            Ticket(Utc(10, 6)) with { ServiceId = 3 },
            Ticket(Utc(10, 7))
        }, Array.Empty<int>(), October, Now, names);

        Assert.Equal(new[] { ((int?)3, "Email", 2, 1), (null, null, 1, 0) },
            report.Breakdowns[ByService].Select(r => (r.Id, r.Name, r.Opened, r.Solved)));
    }

    [Fact]
    public void Rows_with_nothing_in_the_period_are_left_out()
    {
        var report = Calculate(new[] { Ticket(Utc(10, 5), category: 1), Ticket(Utc(8, 5), open: true, category: 2) });

        Assert.Equal(new int?[] { 1 }, report.Breakdowns[ByCategory].Select(r => r.Id));
        Assert.Equal(1, report.Backlog.Open);
    }

    [Fact]
    public void Satisfaction_is_the_average_of_the_surveys_given()
    {
        var report = Calculate(Array.Empty<TicketRow>(), 5, 4, 4);

        Assert.Equal((3, 4.33), (report.Summary.SurveyCount, report.Summary.SatisfactionAverage!.Value));
    }
}
