using System.Net;
using System.Net.Http.Json;
using ItsTool.Domain.Entities;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Every change to who holds which role or permission is in the audit log, with who made it. Before, role
/// assignments and role permissions were left out of the audit entirely, and overrides showed only raw ids.
/// </summary>
[Collection(ApiCollection.Name)]
public class AccessChangeAuditTests
{
    private readonly ApiFixture _api;

    public AccessChangeAuditTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Assigning_and_revoking_a_role_is_audited()
    {
        var (admin, client) = await LoginAsAdminAsync();
        var target = await _api.CreateUserAsync("ticket.view");
        var role = await CreateRoleAsync();

        (await client.PostAsync($"/api/users/{target.Id}/roles/{role.Id}", null)).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/users/{target.Id}/roles/{role.Id}")).EnsureSuccessStatusCode();

        var logs = await UserAuditAsync(target);
        Assert.Collection(logs,
            l => AssertAudit(l, "RoleAssigned", "Roles", "-", role.Name, admin),
            l => AssertAudit(l, "RoleRevoked", "Roles", role.Name, "-", admin));
    }

    [Fact]
    public async Task Permission_overrides_are_audited_with_the_permission_key()
    {
        var (admin, client) = await LoginAsAdminAsync();
        var target = await _api.CreateUserAsync("ticket.view");
        var permission = await CreatePermissionAsync();

        (await client.PostAsync($"/api/users/{target.Id}/permissions/{permission.Id}?isGranted=false", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/users/{target.Id}/permissions/{permission.Id}?isGranted=true", null)).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/users/{target.Id}/permissions/{permission.Id}")).EnsureSuccessStatusCode();

        var logs = await UserAuditAsync(target);
        Assert.Collection(logs,
            l => AssertAudit(l, "PermissionOverrideSet", permission.Key, "-", "Denied", admin),
            l => AssertAudit(l, "PermissionOverrideSet", permission.Key, "Denied", "Granted", admin),
            l => AssertAudit(l, "PermissionOverrideRemoved", permission.Key, "Granted", "-", admin));
    }

    [Fact]
    public async Task Changing_a_roles_permissions_is_audited()
    {
        var (admin, client) = await LoginAsAdminAsync();
        var first = await CreatePermissionAsync();
        var second = await CreatePermissionAsync();
        var name = TestData.Unique("Role");

        var created = await client.PostAsJsonAsync("/api/roles", new { name, description = "", permissions = new[] { first.Key } });
        created.EnsureSuccessStatusCode();
        var roleId = (await created.Content.ReadFromJsonAsync<RoleResponse>())!.Id;
        (await client.PutAsJsonAsync($"/api/roles/{roleId}",
            new { name, description = "", isActive = true, permissions = new[] { second.Key } })).EnsureSuccessStatusCode();

        await using var context = _api.CreateContext();
        var logs = await context.SystemAuditLogs
            .Where(l => l.EntityType == "Role" && l.EntityId == roleId.ToString() && l.FieldName == "Permissions")
            .OrderBy(l => l.Id)
            .ToListAsync();
        Assert.Collection(logs,
            l => AssertAudit(l, "PermissionAdded", "Permissions", "-", first.Key, admin),
            l => AssertAudit(l, "PermissionRemoved", "Permissions", first.Key, "-", admin),
            l => AssertAudit(l, "PermissionAdded", "Permissions", "-", second.Key, admin));
    }

    [Fact]
    public async Task Access_changes_appear_in_the_audit_log_page()
    {
        var (admin, client) = await LoginAsAdminAsync();
        var target = await _api.CreateUserAsync("ticket.view");
        var role = await CreateRoleAsync();
        (await client.PostAsync($"/api/users/{target.Id}/roles/{role.Id}", null)).EnsureSuccessStatusCode();
        var auditor = await _api.LoginAsNewUserAsync("audit.view");

        var page = await auditor.GetFromJsonAsync<AuditPage>($"/api/audit-log?action=RoleAssigned&userId={admin.Id}&page=1&pageSize=50");

        Assert.Contains(page!.Items, i => i.EntityType == "User" && i.EntityId == target.Id.ToString() && i.NewValue == role.Name);
    }

    [Fact]
    public async Task Unknown_users_roles_and_permissions_return_404()
    {
        var (_, client) = await LoginAsAdminAsync();
        var target = await _api.CreateUserAsync("ticket.view");
        var role = await CreateRoleAsync();

        var unknownUser = await client.PostAsync($"/api/users/{int.MaxValue}/roles/{role.Id}", null);
        var unknownRole = await client.PostAsync($"/api/users/{target.Id}/roles/{int.MaxValue}", null);
        var unknownPermission = await client.PostAsync($"/api/users/{target.Id}/permissions/{int.MaxValue}?isGranted=true", null);

        Assert.Equal(HttpStatusCode.NotFound, unknownUser.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRole.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownPermission.StatusCode);
    }

    private async Task<(User Admin, HttpClient Client)> LoginAsAdminAsync()
    {
        var admin = await _api.CreateUserAsync("admin.manage");
        return (admin, await _api.LoginAsync(admin));
    }

    private async Task<Role> CreateRoleAsync()
    {
        await using var context = _api.CreateContext();
        var role = new Role { Name = TestData.Unique("Role") };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        return role;
    }

    private async Task<Permission> CreatePermissionAsync()
    {
        await using var context = _api.CreateContext();
        var key = TestData.Unique("audit.");
        var permission = new Permission { Key = key, Name = key };
        context.Permissions.Add(permission);
        await context.SaveChangesAsync();
        return permission;
    }

    private async Task<List<SystemAuditLog>> UserAuditAsync(User user)
    {
        await using var context = _api.CreateContext();
        return await context.SystemAuditLogs
            .Where(l => l.EntityType == "User" && l.EntityId == user.Id.ToString())
            .OrderBy(l => l.Id)
            .ToListAsync();
    }

    private static void AssertAudit(SystemAuditLog log, string action, string field, string oldValue, string newValue, User by)
    {
        Assert.Equal(action, log.Action);
        Assert.Equal(field, log.FieldName);
        Assert.Equal(oldValue, log.OldValue);
        Assert.Equal(newValue, log.NewValue);
        Assert.Equal(by.Id.ToString(), log.CreatedBy);
    }

    private sealed record RoleResponse(int Id);
    private sealed record AuditPage(List<AuditItem> Items);
    private sealed record AuditItem(string Action, string? EntityType, string? EntityId, string? NewValue);
}
