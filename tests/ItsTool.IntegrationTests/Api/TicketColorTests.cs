using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// The requester chooses the ticket colour, and the pages put it into styles and markup,
/// so the API only accepts #RRGGBB.
/// </summary>
[Collection(ApiCollection.Name)]
public class TicketColorTests
{
    private static readonly string[] EndUser = { "ticket.create", "ticket.view" };

    private readonly ApiFixture _api;

    public TicketColorTests(ApiFixture api) => _api = api;

    [Theory]
    [InlineData("red;background:url(https://evil.example/x)")]
    [InlineData("#1976d2\"><img src=x onerror=alert(1)>")]
    public async Task A_ticket_with_anything_but_a_colour_code_is_refused(string colorHex)
    {
        var client = await _api.LoginAsNewUserAsync(EndUser);
        var ticket = await NewTicketAsync("Printer offline", colorHex);

        var response = await client.PostAsJsonAsync("/api/ticket", ticket);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = _api.CreateContext();
        Assert.False(await context.Tickets.AnyAsync(t => t.ColorHex == colorHex));
    }

    [Theory]
    [InlineData("#1976d2")]
    [InlineData(null)]
    public async Task A_ticket_with_a_colour_code_or_none_is_accepted(string? colorHex)
    {
        var client = await _api.LoginAsNewUserAsync(EndUser);

        var response = await client.PostAsJsonAsync("/api/ticket", await NewTicketAsync("Monitor flickers", colorHex));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<object> NewTicketAsync(string title, string? colorHex)
    {
        await using var context = _api.CreateContext();
        var lookups = await TestData.CreateTicketLookupsAsync(context);
        return new
        {
            title,
            description = "Since this morning",
            projectId = lookups.Project.Id,
            categoryId = lookups.Category.Id,
            typeId = lookups.Type.Id,
            priorityId = lookups.Priority.Id,
            customFields = new Dictionary<string, string>(),
            colorHex
        };
    }
}
