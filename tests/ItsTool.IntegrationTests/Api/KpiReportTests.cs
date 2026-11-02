using System.Net;
using System.Net.Http.Json;
using ItsTool.Application.DTOs;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.SLA;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Services;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// The KPI report covers every ticket, so it needs report.view. Also runs the report's queries against SQL Server,
/// which the in-memory unit tests cannot check. All tests share one database, so each report is filtered to the
/// tickets made here.
/// </summary>
[Collection(ApiCollection.Name)]
public class KpiReportTests
{
    private readonly ApiFixture _api;

    public KpiReportTests(ApiFixture api) => _api = api;

    [Theory]
    [InlineData("/api/reports/kpi")]
    [InlineData("/api/reports/kpi/filters")]
    public async Task Without_report_view_the_kpis_are_refused(string url)
    {
        var agent = await _api.LoginAsNewUserAsync("ticket.view", "ticket.edit", "ticket.assign");

        var response = await agent.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_report_counts_the_filtered_tickets()
    {
        var setup = await CreateTicketsAsync();
        var client = await _api.LoginAsNewUserAsync("report.view");
        var (from, to) = ThisWeek();

        var all = await client.GetFromJsonAsync<KpiReportDto>($"/api/reports/kpi?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&categoryId={setup.CategoryId}");
        var narrowed = await client.GetFromJsonAsync<KpiReportDto>(
            $"/api/reports/kpi?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&categoryId={setup.CategoryId}&priorityId={setup.PriorityId}" +
            $"&departmentId={setup.DepartmentId}&groupId={setup.GroupId}&agentUserId={setup.AgentId}");

        Assert.Equal((2, 1, 1), (all!.Summary.Opened, all.Summary.Solved, all.Summary.Closed));
        Assert.Equal((1, 1), (all.Summary.ResolutionDue, all.Summary.ResolutionMet));
        Assert.Equal(90, all.Summary.MeanTimeToResolveMinutes);
        Assert.Equal((1, 4.0), (all.Summary.SurveyCount, all.Summary.SatisfactionAverage!.Value));
        Assert.Equal(1, all.Backlog.Open);
        Assert.Equal(new[] { setup.GroupName, null }, all.Breakdowns[KpiCalculator.ByTeam].Select(r => r.Name));

        Assert.Equal((1, 1), (narrowed!.Summary.Opened, narrowed.Summary.Solved));
        Assert.Equal(0, narrowed.Backlog.Open);
    }

    [Fact]
    public async Task A_period_the_wrong_way_round_is_a_bad_request()
    {
        var client = await _api.LoginAsNewUserAsync("report.view");

        var response = await client.GetAsync("/api/reports/kpi?from=2026-10-31&to=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_filters_offer_teams_and_their_members()
    {
        var setup = await CreateTicketsAsync();
        var client = await _api.LoginAsNewUserAsync("report.view");

        var options = await client.GetFromJsonAsync<KpiFilterOptionsDto>("/api/reports/kpi/filters");

        Assert.Contains(options!.Teams, o => o.Id == setup.GroupId && o.Name == setup.GroupName);
        Assert.Contains(options.Agents, o => o.Id == setup.AgentId);
        Assert.Contains(options.Departments, o => o.Id == setup.DepartmentId);
    }

    private static (DateOnly From, DateOnly To) ThisWeek()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessCalendar.ResolveTimeZone(null)));
        return (today.AddDays(-3), today.AddDays(3));
    }

    private sealed record Setup(int CategoryId, int PriorityId, int DepartmentId, int GroupId, string GroupName, int AgentId);

    // Two tickets in a category of their own: one solved and closed by an agent of a team, with an SLA and a survey;
    // one open and unassigned
    private async Task<Setup> CreateTicketsAsync()
    {
        await using var context = _api.CreateContext();
        var lookups = await TestData.CreateTicketLookupsAsync(context);
        var closed = new Status { Name = "Closed", ProjectId = lookups.Project.Id, IsClosedStatus = true, IsFinal = true };
        var department = new Department { Name = TestData.Unique("Finance") };
        var group = new Group { Name = TestData.Unique("Network") };
        context.AddRange(closed, department, group);
        var agent = await TestData.CreateUserAsync(context);
        context.GroupMembers.Add(new GroupMember { Group = group, UserId = agent.Id });
        lookups.Requester.Department = department;
        await context.SaveChangesAsync();

        Ticket NewTicket(string title, Status status) => new()
        {
            TicketNumber = TestData.Unique("T").ToUpperInvariant(),
            Title = title,
            Description = "KPI test",
            ProjectId = lookups.Project.Id,
            CategoryId = lookups.Category.Id,
            TypeId = lookups.Type.Id,
            StatusId = status.Id,
            PriorityId = lookups.Priority.Id,
            RequesterUserId = lookups.Requester.Id
        };
        var solved = NewTicket("VPN drops every hour", closed);
        var open = NewTicket("Printer offline", lookups.Status);
        context.Tickets.AddRange(solved, open);
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        context.TicketAssignments.AddRange(
            new TicketAssignment { TicketId = solved.Id, AssignedGroupId = group.Id, AssignedByUserId = agent.Id },
            new TicketAssignment { TicketId = solved.Id, AssignedUserId = agent.Id, AssignedByUserId = agent.Id });
        context.TicketTimeMeasures.Add(new TicketTimeMeasure
        {
            TicketId = solved.Id, ResolvedAt = now, TimeToResolveMinutes = 90, ClosedAt = now, TimeToCloseMinutes = 90, AssignmentCount = 1
        });
        context.TicketSlas.Add(new TicketSla { TicketId = solved.Id, ResolutionDueAt = now.AddHours(4) });
        context.TicketSurveys.Add(new TicketSurvey { TicketId = solved.Id, Rating = 4 });
        await context.SaveChangesAsync();

        return new Setup(lookups.Category.Id, lookups.Priority.Id, department.Id, group.Id, group.Name, agent.Id);
    }
}
