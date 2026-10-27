using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Access changes apply to the next request, not when the token expires two hours later (review A10).
/// /api/audit-log needs audit.view, checked against the token's permission claims.
/// </summary>
[Collection(ApiCollection.Name)]
public class SessionValidationTests
{
    private const string AuditView = "audit.view";

    private readonly ApiFixture _api;

    public SessionValidationTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Removing_a_permission_applies_to_the_next_request()
    {
        var user = await _api.CreateUserAsync(AuditView);
        var client = await _api.LoginAsync(user);
        var before = await client.GetAsync("/api/audit-log");

        await using (var context = _api.CreateContext())
        {
            var grants = await context.RolePermissions
                .Where(rp => rp.Permission!.Key == AuditView && context.UserRoles.Any(ur => ur.UserId == user.Id && ur.RoleId == rp.RoleId))
                .ToListAsync();
            context.RolePermissions.RemoveRange(grants);
            await context.SaveChangesAsync();
        }
        var after = await client.GetAsync("/api/audit-log");

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact]
    public async Task Granting_a_permission_applies_to_the_next_request()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var client = await _api.LoginAsync(user);
        var before = await client.GetAsync("/api/audit-log");

        await using (var context = _api.CreateContext())
        {
            var permission = await context.Permissions.FirstOrDefaultAsync(p => p.Key == AuditView)
                ?? context.Permissions.Add(new Permission { Key = AuditView, Name = AuditView }).Entity;
            context.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, Permission = permission, IsGranted = true });
            await context.SaveChangesAsync();
        }
        var after = await client.GetAsync("/api/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_session()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var client = await _api.LoginAsync(user);

        await UpdateUserAsync(user, u => u.IsActive = false);

        // Ticket search has no account check of its own, so only the session check can refuse it
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ticket/search")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_ends_other_sessions_but_keeps_the_new_one()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var otherDevice = await _api.LoginAsync(user);
        var thisDevice = await _api.LoginAsync(user);
        await Task.Delay(TimeSpan.FromSeconds(1.1)); // token issue times have whole seconds

        var change = await thisDevice.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = ApiFixture.Password, newPassword = "Brand-New-Passphrase-1", confirmPassword = "Brand-New-Passphrase-1" });
        change.EnsureSuccessStatusCode();
        var newToken = (await change.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
        thisDevice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await thisDevice.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_password_reset_ends_the_users_sessions()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var client = await _api.LoginAsync(user);
        var admin = await _api.LoginAsNewUserAsync("admin.manage");
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new { newPassword = "Reset-By-Admin-2026" }))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private async Task UpdateUserAsync(User user, Action<User> change)
    {
        await using var context = _api.CreateContext();
        change(await context.Users.SingleAsync(u => u.Id == user.Id));
        await context.SaveChangesAsync();
    }

    private sealed record TokenResponse(string Token);
}
