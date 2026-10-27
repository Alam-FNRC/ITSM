using System.Net;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// A per-user override wins over the user's roles: a deny takes away a permission a role grants, and a grant
/// adds one no role gives. Before, deny overrides were ignored, although the users page showed them as applied.
/// </summary>
[Collection(ApiCollection.Name)]
public class PermissionOverrideTests
{
    // Needs audit.view and nothing else
    private const string AuditLog = "/api/audit-log?page=1&pageSize=1";

    private readonly ApiFixture _api;

    public PermissionOverrideTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_deny_override_removes_a_permission_the_role_grants_from_the_next_request()
    {
        var user = await _api.CreateUserAsync("audit.view");
        var client = await _api.LoginAsync(user);
        var admin = await _api.LoginAsNewUserAsync("admin.manage");
        var permissionId = await PermissionIdAsync("audit.view");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AuditLog)).StatusCode);

        (await admin.PostAsync($"/api/users/{user.Id}/permissions/{permissionId}?isGranted=false", null)).EnsureSuccessStatusCode();
        var denied = await client.GetAsync(AuditLog);
        (await admin.DeleteAsync($"/api/users/{user.Id}/permissions/{permissionId}")).EnsureSuccessStatusCode();
        var restored = await client.GetAsync(AuditLog);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task A_grant_override_adds_a_permission_no_role_gives()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var client = await _api.LoginAsync(user);
        var admin = await _api.LoginAsNewUserAsync("admin.manage");
        await _api.CreateUserAsync("audit.view");
        var permissionId = await PermissionIdAsync("audit.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(AuditLog)).StatusCode);

        (await admin.PostAsync($"/api/users/{user.Id}/permissions/{permissionId}?isGranted=true", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AuditLog)).StatusCode);
    }

    private async Task<int> PermissionIdAsync(string key)
    {
        await using var context = _api.CreateContext();
        return await context.Permissions.Where(p => p.Key == key).Select(p => p.Id).SingleAsync();
    }
}
