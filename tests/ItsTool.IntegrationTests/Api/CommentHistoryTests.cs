using System.Net.Http.Json;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Ticket history is append-only: deleting and restoring a comment leaves both events on record. Before, the
/// restore deleted the "CommentDeleted" row, so an author could erase a comment and the trace of erasing it.
/// </summary>
[Collection(ApiCollection.Name)]
public class CommentHistoryTests
{
    private readonly ApiFixture _api;

    public CommentHistoryTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Deleting_and_restoring_a_comment_keeps_both_events_in_the_history()
    {
        var requester = await _api.CreateUserAsync("ticket.create", "ticket.view");
        Ticket ticket;
        await using (var context = _api.CreateContext())
        {
            ticket = await TestData.CreateTicketAsync(context, "Monitor flickers", "Second screen", requester.Id);
        }
        var client = await _api.LoginAsync(requester);
        var created = await client.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments", new { content = "It was my fault", isInternal = false });
        var commentId = (await created.Content.ReadFromJsonAsync<CommentResponse>())!.Id;

        (await client.DeleteAsync($"/api/ticket/{ticket.Id}/comments/{commentId}")).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/ticket/{ticket.Id}/comments/{commentId}/restore", null)).EnsureSuccessStatusCode();

        await using var check = _api.CreateContext();
        var actions = await check.TicketHistories
            .Where(h => h.TicketId == ticket.Id && h.FieldName == commentId.ToString())
            .OrderBy(h => h.Id)
            .Select(h => h.Action)
            .ToListAsync();
        Assert.Equal(new[] { "CommentAdded", "CommentDeleted", "CommentRestored" }, actions);
    }

    private sealed record CommentResponse(int Id);
}
