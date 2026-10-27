using System.Net;
using System.Net.Http.Json;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// SignalR sends the token as ?access_token= on WebSocket and server-sent-event connections, which cannot carry
/// headers (review A12). The hub accepts it there; the rest of the API ignores tokens in URLs.
/// </summary>
[Collection(ApiCollection.Name)]
public class HubAuthenticationTests
{
    private const string Negotiate = "/hubs/notification/negotiate?negotiateVersion=1";

    private readonly ApiFixture _api;

    public HubAuthenticationTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task The_hub_accepts_the_token_from_the_query_string()
    {
        var token = await TokenForNewUserAsync();

        var response = await _api.CreateAnonymousClient().PostAsync($"{Negotiate}&access_token={token}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_hub_still_needs_a_token()
    {
        var response = await _api.CreateAnonymousClient().PostAsync(Negotiate, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Other_endpoints_ignore_a_token_in_the_query_string()
    {
        var token = await TokenForNewUserAsync();

        var response = await _api.CreateAnonymousClient().GetAsync($"/api/auth/me?access_token={token}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> TokenForNewUserAsync()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var response = await _api.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { username = user.Username, password = ApiFixture.Password });
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
    }

    private sealed record TokenResponse(string Token);
}
