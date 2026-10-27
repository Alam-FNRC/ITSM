using System.Net.Http.Json;
using ItsTool.Domain.Entities.Ticket;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Internal notes must not reach users without ticket.comment.internal by any route, including the ticket
/// history, which copies comment text when a note is added, edited or deleted.
/// </summary>
[Collection(ApiCollection.Name)]
public class InternalNoteVisibilityTests
{
    private const string NoteText = "Root cause: requester shared their password";
    private const string EditedNoteText = "Root cause: password shared, account reset";

    private readonly ApiFixture _api;

    public InternalNoteVisibilityTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task The_requester_timeline_never_contains_internal_note_text()
    {
        var (ticket, requester, staff) = await CreateTicketWithEditedInternalNoteAsync();

        var timeline = await requester.GetStringAsync($"/api/ticket/{ticket.Id}/timeline");
        var comments = await requester.GetStringAsync($"/api/ticket/{ticket.Id}/comments");

        Assert.DoesNotContain("Root cause", timeline);
        Assert.DoesNotContain("Root cause", comments);
        Assert.Contains("Public reply", timeline);
    }

    [Fact]
    public async Task Staff_with_the_permission_see_internal_notes_in_the_timeline()
    {
        var (ticket, _, staff) = await CreateTicketWithEditedInternalNoteAsync();

        var timeline = await staff.GetStringAsync($"/api/ticket/{ticket.Id}/timeline");

        Assert.Contains(EditedNoteText, timeline);
    }

    private async Task<(Ticket Ticket, HttpClient Requester, HttpClient Staff)> CreateTicketWithEditedInternalNoteAsync()
    {
        var requesterUser = await _api.CreateUserAsync("ticket.create", "ticket.view");
        var staffUser = await _api.CreateUserAsync("ticket.view", "ticket.assign", "ticket.comment.internal");

        Ticket ticket;
        await using (var context = _api.CreateContext())
        {
            ticket = await TestData.CreateTicketAsync(context, "Account locked", "Cannot log in", requesterUser.Id);
            context.TicketAssignments.Add(new TicketAssignment
            {
                TicketId = ticket.Id,
                AssignedUserId = staffUser.Id,
                AssignedByUserId = requesterUser.Id
            });
            await context.SaveChangesAsync();
        }

        var staff = await _api.LoginAsync(staffUser);
        var note = await staff.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments", new { content = NoteText, isInternal = true });
        note.EnsureSuccessStatusCode();
        var noteId = (await note.Content.ReadFromJsonAsync<CommentResponse>())!.Id;
        (await staff.PutAsJsonAsync($"/api/ticket/{ticket.Id}/comments/{noteId}", new { content = EditedNoteText }))
            .EnsureSuccessStatusCode();
        (await staff.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments", new { content = "Public reply", isInternal = false }))
            .EnsureSuccessStatusCode();

        return (ticket, await _api.LoginAsync(requesterUser), staff);
    }

    private sealed record CommentResponse(int Id);
}
