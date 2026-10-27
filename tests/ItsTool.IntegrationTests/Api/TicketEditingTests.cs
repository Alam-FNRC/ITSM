using System.Net;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Within a ticket they can see, users may only change it with ticket.edit or ticket.assign, and may only write
/// internal notes with ticket.comment.internal (review A3).
/// </summary>
[Collection(ApiCollection.Name)]
public class TicketEditingTests
{
    private static readonly string[] EndUser = { "ticket.create", "ticket.view" };
    // ticket.assign is what makes assigned tickets visible to an agent (the search rule)
    private static readonly string[] Agent = { "ticket.view", "ticket.assign" };

    private readonly ApiFixture _api;

    public TicketEditingTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task The_requester_cannot_change_the_ticket_without_edit_permission()
    {
        var requester = await _api.CreateUserAsync(EndUser);
        var ticket = await CreateTicketAsync(requester);
        var client = await _api.LoginAsync(requester);

        var response = await client.PutAsJsonAsync($"/api/ticket/{ticket.Id}", Edit(ticket, "Raised my own priority"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ticket.Title, await TitleOfAsync(ticket));
    }

    [Fact]
    public async Task An_assigned_agent_can_change_the_ticket()
    {
        var ticket = await CreateTicketAsync(await _api.CreateUserAsync(EndUser));
        var agent = await _api.CreateUserAsync(Agent);
        await AssignAsync(ticket, agent);
        var client = await _api.LoginAsync(agent);

        var response = await client.PutAsJsonAsync($"/api/ticket/{ticket.Id}", Edit(ticket, "Laptop will not boot (BIOS)"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Laptop will not boot (BIOS)", await TitleOfAsync(ticket));
    }

    [Fact]
    public async Task The_requester_can_comment_but_not_post_internal_notes()
    {
        var requester = await _api.CreateUserAsync(EndUser);
        var ticket = await CreateTicketAsync(requester);
        var client = await _api.LoginAsync(requester);

        var comment = await client.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments", new { content = "Any update?", isInternal = false });
        var internalNote = await client.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments", new { content = "Fake staff note", isInternal = true });

        Assert.Equal(HttpStatusCode.OK, comment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, internalNote.StatusCode);
        await using var context = _api.CreateContext();
        Assert.False(await context.TicketComments.AnyAsync(c => c.TicketId == ticket.Id && c.IsInternal));
    }

    [Fact]
    public async Task Staff_with_the_internal_note_permission_can_post_internal_notes()
    {
        var ticket = await CreateTicketAsync(await _api.CreateUserAsync(EndUser));
        var agent = await _api.CreateUserAsync(Agent.Append("ticket.comment.internal").ToArray());
        await AssignAsync(ticket, agent);
        var client = await _api.LoginAsync(agent);

        var response = await client.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments", new { content = "Reimage needed", isInternal = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = _api.CreateContext();
        Assert.True(await context.TicketComments.AnyAsync(c => c.TicketId == ticket.Id && c.IsInternal));
    }

    private static object Edit(Ticket ticket, string title) => new
    {
        title,
        description = ticket.Description,
        categoryId = ticket.CategoryId,
        priorityId = ticket.PriorityId,
        customFields = new Dictionary<string, string>()
    };

    private async Task<Ticket> CreateTicketAsync(User requester)
    {
        await using var context = _api.CreateContext();
        return await TestData.CreateTicketAsync(context, "Laptop will not boot", "Black screen after the update", requester.Id);
    }

    private async Task AssignAsync(Ticket ticket, User agent)
    {
        await using var context = _api.CreateContext();
        context.TicketAssignments.Add(new TicketAssignment
        {
            TicketId = ticket.Id,
            AssignedUserId = agent.Id,
            AssignedByUserId = ticket.RequesterUserId
        });
        await context.SaveChangesAsync();
    }

    private async Task<string> TitleOfAsync(Ticket ticket)
    {
        await using var context = _api.CreateContext();
        return (await context.Tickets.SingleAsync(t => t.Id == ticket.Id)).Title;
    }
}
