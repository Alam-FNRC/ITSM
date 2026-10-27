using System.Net;
using System.Net.Http.Json;

namespace ItsTool.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class AuthenticationTests
{
    private readonly ApiFixture _api;

    public AuthenticationTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Requests_without_a_token_are_rejected()
    {
        var response = await _api.CreateAnonymousClient().GetAsync("/api/ticket/search");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requests_with_a_forged_token_are_rejected()
    {
        var client = _api.CreateAnonymousClient();
        // Signed with a key the API does not hold
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwicm9sZSI6IlN1cGVyQWRtaW4ifQ.c2lnbmF0dXJl");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_logged_in_user_gets_their_own_profile()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var client = await _api.LoginAsync(user);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.Equal(user.Username, me!.Username);
        Assert.Contains("ticket.view", me.Permissions);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        var user = await _api.CreateUserAsync();

        var response = await _api.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { username = user.Username, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record MeResponse(string Username, List<string> Permissions);
}
