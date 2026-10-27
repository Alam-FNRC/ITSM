using System.Net;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Five wrong passwords in a row lock the account for 15 minutes, during which even the right password is
/// refused (review A9). The defaults apply because the test API does not override Auth:* settings.
/// </summary>
[Collection(ApiCollection.Name)]
public class LoginLockoutTests
{
    private readonly ApiFixture _api;

    public LoginLockoutTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_right_one()
    {
        var user = await _api.CreateUserAsync("ticket.view");

        for (var i = 0; i < 5; i++) await LoginAsync(user, "wrong-password");
        var rightPassword = await LoginAsync(user, ApiFixture.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, rightPassword.StatusCode);
        await using var context = _api.CreateContext();
        var events = await context.AuthEvents.Where(e => e.UserId == user.Id).OrderBy(e => e.Id).ToListAsync();
        Assert.Contains(events, e => e.EventType == AuthEventTypes.AccountLocked);
        Assert.Equal(AuthFailureReasons.LockedOut, events.Last().FailureReason);
        var locked = await context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.InRange(locked.LockoutEndsAt!.Value, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task The_account_opens_again_when_the_lockout_ends()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        await SetLockoutAsync(user, DateTime.UtcNow.AddSeconds(-1));

        var response = await LoginAsync(user, ApiFixture.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var context = _api.CreateContext();
        Assert.Null((await context.Users.SingleAsync(u => u.Id == user.Id)).LockoutEndsAt);
    }

    [Fact]
    public async Task A_successful_login_resets_the_count_of_wrong_passwords()
    {
        var user = await _api.CreateUserAsync("ticket.view");

        for (var i = 0; i < 4; i++) await LoginAsync(user, "wrong-password");
        (await LoginAsync(user, ApiFixture.Password)).EnsureSuccessStatusCode();
        for (var i = 0; i < 4; i++) await LoginAsync(user, "wrong-password");
        var response = await LoginAsync(user, ApiFixture.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_administrator_password_reset_unlocks_the_account()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        await SetLockoutAsync(user, DateTime.UtcNow.AddMinutes(10));
        var admin = await _api.LoginAsNewUserAsync("admin.manage");

        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new { newPassword = "Reset-Password-2026" }))
            .EnsureSuccessStatusCode();
        var response = await _api.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login",
            new { username = user.Username, password = "Reset-Password-2026" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> LoginAsync(User user, string password) =>
        _api.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login", new { username = user.Username, password });

    private async Task SetLockoutAsync(User user, DateTime endsAt)
    {
        await using var context = _api.CreateContext();
        (await context.Users.SingleAsync(u => u.Id == user.Id)).LockoutEndsAt = endsAt;
        await context.SaveChangesAsync();
    }
}
