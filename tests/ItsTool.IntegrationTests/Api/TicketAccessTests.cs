using System.Net;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// A ticket is visible to its requester, to agents it is assigned to (directly or through a group), and to
/// holders of report.view. Everyone else gets 404 from every ticket endpoint (review A1, A3).
/// </summary>
[Collection(ApiCollection.Name)]
public class TicketAccessTests
{
    private static readonly string[] EndUser = { "ticket.create", "ticket.view" };
    private static readonly string[] Agent = { "ticket.view", "ticket.edit", "ticket.assign", "ticket.comment" };

    private readonly ApiFixture _api;

    public TicketAccessTests(ApiFixture api) => _api = api;

    [Theory]
    [InlineData("")]
    [InlineData("/timeline")]
    [InlineData("/comments")]
    [InlineData("/attachments")]
    [InlineData("/eligible-users")]
    [InlineData("/assignments/tree")]
    [InlineData("/allowed-transitions")]
    public async Task Another_end_user_cannot_read_the_ticket(string endpoint)
    {
        var ticket = await CreateTicketAsync();
        var otherUser = await _api.LoginAsNewUserAsync(EndUser);

        var response = await otherUser.GetAsync($"/api/ticket/{ticket.Id}{endpoint}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_requester_can_read_their_ticket()
    {
        var requester = await _api.CreateUserAsync(EndUser);
        var ticket = await CreateTicketAsync(requester);
        var client = await _api.LoginAsync(requester);

        var response = await client.GetAsync($"/api/ticket/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_user_cannot_edit_the_ticket()
    {
        var ticket = await CreateTicketAsync();
        var otherAgent = await _api.LoginAsNewUserAsync(Agent);

        var response = await otherAgent.PutAsJsonAsync($"/api/ticket/{ticket.Id}", new
        {
            title = "Hijacked",
            description = "Changed by someone without access",
            categoryId = ticket.CategoryId,
            priorityId = ticket.PriorityId,
            customFields = new Dictionary<string, string>()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var context = _api.CreateContext();
        Assert.Equal(ticket.Title, (await context.Tickets.SingleAsync(t => t.Id == ticket.Id)).Title);
    }

    [Fact]
    public async Task Another_user_cannot_comment_on_the_ticket()
    {
        var ticket = await CreateTicketAsync();
        var otherUser = await _api.LoginAsNewUserAsync(EndUser);

        var response = await otherUser.PostAsJsonAsync($"/api/ticket/{ticket.Id}/comments",
            new { content = "Not my ticket", isInternal = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var context = _api.CreateContext();
        Assert.False(await context.TicketComments.AnyAsync(c => c.TicketId == ticket.Id));
    }

    [Fact]
    public async Task An_agent_sees_tickets_assigned_to_their_group_but_not_others()
    {
        var ticket = await CreateTicketAsync();
        var groupAgent = await _api.CreateUserAsync(Agent);
        var otherAgent = await _api.CreateUserAsync(Agent);
        await AssignToNewGroupAsync(ticket, groupAgent);

        var groupAgentResponse = await (await _api.LoginAsync(groupAgent)).GetAsync($"/api/ticket/{ticket.Id}");
        var otherAgentResponse = await (await _api.LoginAsync(otherAgent)).GetAsync($"/api/ticket/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, groupAgentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherAgentResponse.StatusCode);
    }

    [Fact]
    public async Task A_report_viewer_sees_every_ticket()
    {
        var ticket = await CreateTicketAsync();
        var manager = await _api.LoginAsNewUserAsync("ticket.view", "report.view");

        var response = await manager.GetAsync($"/api/ticket/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Attachments_cannot_be_downloaded_without_logging_in()
    {
        var ticket = await CreateTicketAsync();
        await using var attachment = await AttachmentFile.CreateAsync(_api, ticket);

        var response = await _api.CreateAnonymousClient().GetAsync(attachment.DownloadUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Attachments_cannot_be_downloaded_by_another_user()
    {
        var ticket = await CreateTicketAsync();
        await using var attachment = await AttachmentFile.CreateAsync(_api, ticket);
        var otherUser = await _api.LoginAsNewUserAsync(EndUser);

        var response = await otherUser.GetAsync(attachment.DownloadUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_requester_can_download_their_attachment()
    {
        var requester = await _api.CreateUserAsync(EndUser);
        var ticket = await CreateTicketAsync(requester);
        await using var attachment = await AttachmentFile.CreateAsync(_api, ticket);
        var client = await _api.LoginAsync(requester);

        var body = await client.GetStringAsync(attachment.DownloadUrl);

        Assert.Equal(AttachmentFile.Content, body);
    }

    private async Task<Ticket> CreateTicketAsync(User? requester = null)
    {
        requester ??= await _api.CreateUserAsync(EndUser);
        await using var context = _api.CreateContext();
        return await TestData.CreateTicketAsync(context, "Laptop will not boot", "Black screen after the update", requester.Id);
    }

    private async Task AssignToNewGroupAsync(Ticket ticket, User member)
    {
        await using var context = _api.CreateContext();
        var group = new Group { Name = TestData.Unique("Desk") };
        context.Groups.Add(group);
        context.GroupMembers.Add(new GroupMember { Group = group, UserId = member.Id });
        context.TicketAssignments.Add(new TicketAssignment
        {
            TicketId = ticket.Id,
            AssignedGroup = group,
            AssignedByUserId = ticket.RequesterUserId
        });
        await context.SaveChangesAsync();
    }

    /// <summary>A file on disk plus its attachment row, removed again when the test ends.</summary>
    private sealed class AttachmentFile : IAsyncDisposable
    {
        public const string Content = "confidential attachment body";

        private readonly string _path;

        private AttachmentFile(string path, string downloadUrl)
        {
            _path = path;
            DownloadUrl = downloadUrl;
        }

        public string DownloadUrl { get; }

        public static async Task<AttachmentFile> CreateAsync(ApiFixture api, Ticket ticket)
        {
            var path = Path.Combine(Path.GetTempPath(), $"itsm-test-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(path, Content);

            await using var context = api.CreateContext();
            var attachment = new TicketAttachment
            {
                TicketId = ticket.Id,
                FileName = "notes.txt",
                FilePath = path,
                FileSize = Content.Length,
                ContentType = "text/plain",
                UploadedByUserId = ticket.RequesterUserId
            };
            context.TicketAttachments.Add(attachment);
            await context.SaveChangesAsync();

            return new AttachmentFile(path, $"/api/ticket/{ticket.Id}/attachments/{attachment.Id}/download");
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
