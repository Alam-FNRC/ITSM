using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Uploads are stored under a server-made name inside the upload folder, only allowed types with matching content
/// are accepted, and downloads carry a content type chosen by the server (review A11).
/// </summary>
[Collection(ApiCollection.Name)]
public class AttachmentUploadTests
{
    private readonly ApiFixture _api;

    public AttachmentUploadTests(ApiFixture api) => _api = api;

    [Theory]
    [InlineData("x/../../../escape.txt")]
    [InlineData(@"x\..\..\..\escape.txt")]
    public async Task A_file_name_with_folders_cannot_leave_the_upload_folder(string fileName)
    {
        var (ticket, client) = await CreateTicketAsRequesterAsync();

        var response = await UploadAsync(client, ticket, fileName, "text/plain", "harmless text");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attachment = await LatestAttachmentAsync(ticket);
        Assert.StartsWith(Path.GetFullPath(_api.UploadsPath), Path.GetFullPath(attachment.FilePath));
        Assert.DoesNotContain("escape", Path.GetFileName(attachment.FilePath));
        Assert.Equal("escape.txt", attachment.FileName);
    }

    [Fact]
    public async Task The_client_cannot_choose_the_content_type_the_file_is_served_with()
    {
        var (ticket, client) = await CreateTicketAsRequesterAsync();

        (await UploadAsync(client, ticket, "notes.txt", "text/html", "<script>alert(document.cookie)</script>")).EnsureSuccessStatusCode();
        var attachment = await LatestAttachmentAsync(ticket);
        var download = await client.GetAsync($"/api/ticket/{ticket.Id}/attachments/{attachment.Id}/download");

        Assert.Equal("text/plain", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Theory]
    [InlineData("photo.png", "image/png", "<html><script>alert(1)</script></html>")]
    [InlineData("report.pdf", "application/pdf", "not really a pdf")]
    [InlineData("drawing.svg", "image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("tool.exe", "application/octet-stream", "MZ")]
    public async Task Disallowed_types_and_disguised_content_are_refused(string fileName, string contentType, string body)
    {
        var (ticket, client) = await CreateTicketAsRequesterAsync();

        var response = await UploadAsync(client, ticket, fileName, contentType, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = _api.CreateContext();
        Assert.False(await context.TicketAttachments.AnyAsync(a => a.TicketId == ticket.Id));
    }

    [Fact]
    public async Task A_real_image_is_accepted_and_served_as_an_image()
    {
        var (ticket, client) = await CreateTicketAsRequesterAsync();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52 };

        (await UploadAsync(client, ticket, "screen.png", "application/octet-stream", png)).EnsureSuccessStatusCode();
        var attachment = await LatestAttachmentAsync(ticket);
        var download = await client.GetAsync($"/api/ticket/{ticket.Id}/attachments/{attachment.Id}/download");

        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await download.Content.ReadAsByteArrayAsync());
    }

    private async Task<(Ticket Ticket, HttpClient Client)> CreateTicketAsRequesterAsync()
    {
        var requester = await _api.CreateUserAsync("ticket.create", "ticket.view");
        await using var context = _api.CreateContext();
        var ticket = await TestData.CreateTicketAsync(context, "Printer jam", "Tray 2", requester.Id);
        return (ticket, await _api.LoginAsync(requester));
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Ticket ticket, string fileName, string contentType, string body) =>
        UploadAsync(client, ticket, fileName, contentType, Encoding.UTF8.GetBytes(body));

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Ticket ticket, string fileName, string contentType, byte[] body)
    {
        var file = new ByteArrayContent(body);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        return await client.PostAsync($"/api/ticket/{ticket.Id}/attachments", form);
    }

    private async Task<TicketAttachment> LatestAttachmentAsync(Ticket ticket)
    {
        await using var context = _api.CreateContext();
        return await context.TicketAttachments.Where(a => a.TicketId == ticket.Id).OrderByDescending(a => a.Id).FirstAsync();
    }
}
