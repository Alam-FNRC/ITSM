using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Password changes need the current password and meet the policy (12+ characters); passwords an administrator
/// sets must be replaced at next login, and the API itself enforces that (review A6).
/// </summary>
[Collection(ApiCollection.Name)]
public class PasswordChangeTests
{
    private const string Temporary = "Temporary-Pass-2026";
    private const string Chosen = "My-Own-Passphrase-2026";

    private readonly ApiFixture _api;

    public PasswordChangeTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_user_created_by_an_administrator_must_change_the_password_before_using_the_api()
    {
        var username = await CreateUserAsAdminAsync(Temporary);
        var (client, mustChange) = await LoginAsync(username, Temporary);

        var search = await client.GetAsync("/api/ticket/search");
        var me = await client.GetAsync("/api/auth/me");

        Assert.True(mustChange);
        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task After_changing_the_password_the_api_opens_up()
    {
        var username = await CreateUserAsAdminAsync(Temporary);
        var (client, _) = await LoginAsync(username, Temporary);

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Temporary, newPassword = Chosen, confirmPassword = Chosen });
        change.EnsureSuccessStatusCode();
        var newToken = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken.Token);

        Assert.False(newToken.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ticket/search")).StatusCode);
        await using var context = _api.CreateContext();
        Assert.Contains(await context.AuthEvents.Where(e => e.Username == username).Select(e => e.EventType).ToListAsync(),
            type => type == AuthEventTypes.PasswordChanged);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-current-password")]
    public async Task Changing_the_password_needs_the_current_one(string? currentPassword)
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var client = await _api.LoginAsync(user);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword, newPassword = Chosen, confirmPassword = Chosen });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var login = await _api.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { username = user.Username, password = ApiFixture.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Theory]
    [InlineData("Short-1")]
    [InlineData("contains-{username}-inside")]
    public async Task Weak_passwords_are_refused(string pattern)
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var client = await _api.LoginAsync(user);
        var weak = pattern.Replace("{username}", user.Username);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = ApiFixture.Password, newPassword = weak, confirmPassword = weak });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Administrators_cannot_set_weak_passwords_and_resets_are_audited()
    {
        var admin = await _api.CreateUserAsync("admin.manage");
        var adminClient = await _api.LoginAsync(admin);
        var target = await _api.CreateUserAsync("ticket.view");

        var weakCreate = await adminClient.PostAsJsonAsync("/api/users", NewUser(TestData.Unique("weak"), "Short-1"));
        var weakReset = await adminClient.PostAsJsonAsync($"/api/users/{target.Id}/reset-password", new { newPassword = "Short-1" });
        var reset = await adminClient.PostAsJsonAsync($"/api/users/{target.Id}/reset-password", new { newPassword = Temporary });

        Assert.Equal(HttpStatusCode.BadRequest, weakCreate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weakReset.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        await using var context = _api.CreateContext();
        var audit = await context.SystemAuditLogs.SingleAsync(l => l.EntityType == "User" && l.EntityId == target.Id.ToString() && l.Action == "PasswordReset");
        Assert.Equal(admin.Id.ToString(), audit.CreatedBy);
    }

    private async Task<string> CreateUserAsAdminAsync(string password)
    {
        var admin = await _api.LoginAsNewUserAsync("admin.manage");
        var username = TestData.Unique("staff");
        (await admin.PostAsJsonAsync("/api/users", NewUser(username, password))).EnsureSuccessStatusCode();
        return username;
    }

    private async Task<(HttpClient Client, bool MustChangePassword)> LoginAsync(string username, string password)
    {
        var client = _api.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return (client, login.MustChangePassword);
    }

    private static object NewUser(string username, string password) => new
    {
        username,
        email = username + "@fnrc.test",
        firstName = "New",
        lastName = "Staff",
        password,
        departmentId = (int?)null,
        profilePhoto = (string?)null,
        groupIds = Array.Empty<int>()
    };

    private sealed record LoginResponse(string Token, bool MustChangePassword);
}
