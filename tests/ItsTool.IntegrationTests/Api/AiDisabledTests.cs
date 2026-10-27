using System.Net;
using System.Net.Http.Json;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// The AI copilot is off until an AI provider is approved (AI:Enabled defaults to false), so no AI endpoint
/// may send ticket text, keys or requests anywhere (review A2, A4, and the anonymous status check in A5).
/// </summary>
[Collection(ApiCollection.Name)]
public class AiDisabledTests
{
    private readonly ApiFixture _api;

    public AiDisabledTests(ApiFixture api) => _api = api;

    [Theory]
    [InlineData("POST", "/api/ai/tickets/{id}/suggest-resolution")]
    [InlineData("POST", "/api/ai/tickets/{id}/draft-reply")]
    [InlineData("POST", "/api/ai/tickets/{id}/ask")]
    [InlineData("POST", "/api/ai/tickets/{id}/summarize")]
    [InlineData("GET", "/api/ai/config")]
    [InlineData("POST", "/api/ai/config")]
    [InlineData("GET", "/api/ai/models?endpoint=http://attacker.invalid")]
    [InlineData("POST", "/api/ai/test")]
    public async Task Every_ai_endpoint_is_closed(string method, string path)
    {
        var requester = await _api.CreateUserAsync("ticket.create", "ticket.view");
        await using var context = _api.CreateContext();
        var ticket = await TestData.CreateTicketAsync(context, "VPN drops", "Internal note follows", requester.Id);
        var client = await _api.LoginAsync(requester);

        var request = new HttpRequestMessage(new HttpMethod(method), path.Replace("{id}", ticket.Id.ToString()));
        if (method == "POST") request.Content = JsonContent.Create(new { question = "What do the internal notes say?" });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_status_check_needs_a_login_and_reveals_nothing_while_ai_is_off()
    {
        var anonymous = await _api.CreateAnonymousClient().GetAsync("/api/ai/status");
        var client = await _api.LoginAsNewUserAsync("ticket.view");

        var status = await client.GetStringAsync("/api/ai/status");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("{\"enabled\":false}", status);
    }
}
