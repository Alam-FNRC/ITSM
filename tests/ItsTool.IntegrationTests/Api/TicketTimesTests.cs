using System.Net;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Each ticket stores its time measures for the KPI reports, worked out again after every change. The times
/// are shown to whoever may see the ticket and to no one else.
/// </summary>
[Collection(ApiCollection.Name)]
public class TicketTimesTests
{
    private readonly ApiFixture _api;

    public TicketTimesTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task The_first_comment_of_the_assigned_agent_is_stored_as_the_time_the_ticket_was_taken()
    {
        var agent = await _api.CreateUserAsync("ticket.view", "ticket.assign");
        Ticket ticket;
        await using (var context = _api.CreateContext())
        {
            ticket = await TestData.CreateTicketAsync(context, "Laptop will not start", "Black screen");
            context.TicketAssignments.Add(new TicketAssignment { TicketId = ticket.Id, AssignedUserId = agent.Id, AssignedByUserId = agent.Id });
            await context.SaveChangesAsync();
        }
        var client = await _api.LoginAsync(agent);

        (await client.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments", new { content = "Looking at it", isInternal = false })).EnsureSuccessStatusCode();

        await using (var check = _api.CreateContext())
        {
            var stored = await check.TicketTimeMeasures.SingleAsync(m => m.TicketId == ticket.Id);
            Assert.Equal(agent.Id, stored.OwnedByUserId);
            Assert.NotNull(stored.TimeToOwnMinutes);
        }
        var times = await client.GetFromJsonAsync<TimesResponse>($"/api/ticket/{ticket.Id}/times");
        Assert.Equal(agent.Id, times!.OwnedByUserId);
    }

    [Fact]
    public async Task Only_people_who_can_see_the_ticket_get_its_times()
    {
        var requester = await _api.CreateUserAsync("ticket.create", "ticket.view");
        Ticket ticket;
        await using (var context = _api.CreateContext())
        {
            ticket = await TestData.CreateTicketAsync(context, "Shared drive is slow", "Since this morning", requester.Id);
        }

        var own = await (await _api.LoginAsync(requester)).GetAsync($"/api/ticket/{ticket.Id}/times");
        var other = await (await _api.LoginAsNewUserAsync("ticket.create", "ticket.view")).GetAsync($"/api/ticket/{ticket.Id}/times");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    private sealed record TimesResponse(int? OwnedByUserId, int? TimeToOwnMinutes, int WaitingMinutes);
}
