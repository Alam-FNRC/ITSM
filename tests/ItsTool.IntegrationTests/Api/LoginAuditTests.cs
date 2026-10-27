using System.Net;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>Every login attempt is recorded in AuthEvents, with who, from where and why it failed (review A9).</summary>
[Collection(ApiCollection.Name)]
public class LoginAuditTests
{
    private const string Browser = "IntegrationTest/1.0";

    private readonly ApiFixture _api;

    public LoginAuditTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_successful_login_is_recorded()
    {
        var user = await _api.CreateUserAsync("ticket.view");

        var response = await LoginAsync(user.Username, ApiFixture.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recorded = Assert.Single(await EventsForAsync(user.Username));
        Assert.Equal(AuthEventTypes.LoginSucceeded, recorded.EventType);
        Assert.Equal(user.Id, recorded.UserId);
        Assert.Null(recorded.FailureReason);
        Assert.Equal(Browser, recorded.UserAgent);
    }

    [Fact]
    public async Task A_wrong_password_is_recorded_against_the_account()
    {
        var user = await _api.CreateUserAsync("ticket.view");

        var response = await LoginAsync(user.Username, "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var recorded = Assert.Single(await EventsForAsync(user.Username));
        Assert.Equal(AuthEventTypes.LoginFailed, recorded.EventType);
        Assert.Equal(AuthFailureReasons.WrongPassword, recorded.FailureReason);
        Assert.Equal(user.Id, recorded.UserId);
    }

    [Fact]
    public async Task An_unknown_name_is_recorded_as_typed()
    {
        var typed = TestData.Unique("nobody");

        var response = await LoginAsync(typed, "anything");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var recorded = Assert.Single(await EventsForAsync(typed));
        Assert.Equal(AuthFailureReasons.UnknownUser, recorded.FailureReason);
        Assert.Null(recorded.UserId);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_log_in_and_the_attempt_is_recorded()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        await using (var context = _api.CreateContext())
        {
            (await context.Users.SingleAsync(u => u.Id == user.Id)).IsActive = false;
            await context.SaveChangesAsync();
        }

        var response = await LoginAsync(user.Username, ApiFixture.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AuthFailureReasons.InactiveAccount, Assert.Single(await EventsForAsync(user.Username)).FailureReason);
    }

    private Task<HttpResponseMessage> LoginAsync(string username, string password)
    {
        var client = _api.CreateAnonymousClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
        return client.PostAsJsonAsync("/api/auth/login", new { username, password });
    }

    private async Task<List<AuthEvent>> EventsForAsync(string username)
    {
        await using var context = _api.CreateContext();
        return await context.AuthEvents.Where(e => e.Username == username).ToListAsync();
    }
}
