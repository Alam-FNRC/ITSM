using System.Net;
using System.Net.Http.Json;
using ItsTool.Application.Constants;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Security incidents are seen by holders of security.incident.view (the security team and the IT manager) and by
/// people added to one incident, and by no one else: not the reporter, not other staff, not even SuperAdmin
/// without a grant. Everyone else gets "not found", and nothing about an incident reaches the system audit log.
/// </summary>
[Collection(ApiCollection.Name)]
public class SecurityIncidentAccessTests
{
    private const string View = PermissionConstants.SecurityIncidentView;
    private const string Manage = PermissionConstants.SecurityIncidentManage;
    private readonly ApiFixture _api;

    public SecurityIncidentAccessTests(ApiFixture api) => _api = api;

    private async Task<(int Id, string Reference, string Title)> ReportAsync(HttpClient reporter)
    {
        var title = TestData.Unique("Suspicious e-mail from the director ");
        var response = await reporter.PostAsJsonAsync("/api/security/reports", new { title, description = "Asked me to buy gift cards", occurredAt = (DateTime?)null });
        response.EnsureSuccessStatusCode();
        var report = (await response.Content.ReadFromJsonAsync<MyReport>())!;
        await using var context = _api.CreateContext();
        var id = await context.SecurityIncidents.Where(i => i.Reference == report.Reference).Select(i => i.Id).SingleAsync();
        return (id, report.Reference, title);
    }

    private async Task<User> CreateSuperAdminAsync()
    {
        var user = await _api.CreateUserAsync();
        await using var context = _api.CreateContext();
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == "SuperAdmin") ?? context.Roles.Add(new Role { Name = "SuperAdmin" }).Entity;
        context.UserRoles.Add(new UserRole { UserId = user.Id, Role = role });
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Staff_can_report_and_follow_their_report_but_not_open_the_incident()
    {
        var reporter = await _api.LoginAsNewUserAsync("ticket.create");
        var (id, reference, _) = await ReportAsync(reporter);

        var mine = await reporter.GetFromJsonAsync<List<MyReport>>("/api/security/reports/mine");
        Assert.Contains(mine!, r => r.Reference == reference && r.Progress == "Received");
        Assert.Equal(HttpStatusCode.NotFound, (await reporter.GetAsync($"/api/security/incidents/{id}")).StatusCode);
        Assert.Empty((await reporter.GetFromJsonAsync<List<ListItem>>("/api/security/incidents"))!);
    }

    [Fact]
    public async Task Other_staff_and_a_SuperAdmin_without_a_grant_cannot_see_it()
    {
        var (id, _, title) = await ReportAsync(await _api.LoginAsNewUserAsync());
        var colleague = await _api.LoginAsNewUserAsync("ticket.view", "report.view", "audit.view");
        var superAdmin = await _api.LoginAsync(await CreateSuperAdminAsync());

        foreach (var client in new[] { colleague, superAdmin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/security/incidents/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/security/incidents/{id}/access-log")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/security/incidents/{id}/notes", new { text = "x" })).StatusCode);
            Assert.DoesNotContain((await client.GetFromJsonAsync<List<ListItem>>("/api/security/incidents"))!, i => i.Id == id);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/security/incidents", new { title, description = "x" })).StatusCode);
        }
    }

    [Fact]
    public async Task A_viewer_sees_everything_but_changes_nothing()
    {
        var (id, _, _) = await ReportAsync(await _api.LoginAsNewUserAsync());
        var itManager = await _api.LoginAsNewUserAsync(View);

        Assert.Equal(HttpStatusCode.OK, (await itManager.GetAsync($"/api/security/incidents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await itManager.GetAsync($"/api/security/incidents/{id}/access-log")).StatusCode);
        Assert.Contains((await itManager.GetFromJsonAsync<List<ListItem>>("/api/security/incidents"))!, i => i.Id == id);
        Assert.Equal(HttpStatusCode.Forbidden, (await itManager.PostAsJsonAsync($"/api/security/incidents/{id}/triage", new { outcome = "Event", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await itManager.PostAsJsonAsync($"/api/security/incidents/{id}/notes", new { text = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_member_works_on_their_incident_only_until_removed()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var (id, _, _) = await ReportAsync(await _api.LoginAsNewUserAsync());
        var (otherId, _, _) = await ReportAsync(await _api.LoginAsNewUserAsync());
        var adminUser = await _api.CreateUserAsync();
        var serverAdmin = await _api.LoginAsync(adminUser);

        (await team.PostAsJsonAsync($"/api/security/incidents/{id}/triage", new { outcome = "Incident", reason = "Malware found", category = "Malware", severity = 3 })).EnsureSuccessStatusCode();
        (await team.PostAsJsonAsync($"/api/security/incidents/{id}/affected-items", new { items = new[] { new { kind = "Server", name = "fs01" } } })).EnsureSuccessStatusCode();
        (await team.PostAsJsonAsync($"/api/security/incidents/{id}/members", new { userId = adminUser.Id })).EnsureSuccessStatusCode();

        var detail = (await serverAdmin.GetFromJsonAsync<Detail>($"/api/security/incidents/{id}"))!;
        Assert.False(detail.CanManage);
        Assert.Equal(HttpStatusCode.NoContent, (await serverAdmin.PostAsJsonAsync($"/api/security/incidents/{id}/notes", new { text = "Patched and rebooted" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await serverAdmin.PutAsJsonAsync($"/api/security/incidents/{id}/affected-items/{detail.AffectedItems[0].Id}", new { status = "Fixed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await serverAdmin.PostAsJsonAsync($"/api/security/incidents/{id}/status", new { status = "Recovery" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await serverAdmin.GetAsync($"/api/security/incidents/{id}/access-log")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await serverAdmin.GetAsync($"/api/security/incidents/{otherId}")).StatusCode);
        Assert.Equal(new[] { id }, (await serverAdmin.GetFromJsonAsync<List<ListItem>>("/api/security/incidents"))!.Select(i => i.Id));

        (await team.DeleteAsync($"/api/security/incidents/{id}/members/{adminUser.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await serverAdmin.GetAsync($"/api/security/incidents/{id}")).StatusCode);
    }

    [Fact]
    public async Task Every_opening_is_recorded_and_nothing_reaches_the_system_audit_log()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var (id, reference, title) = await ReportAsync(await _api.LoginAsNewUserAsync());

        await team.GetAsync($"/api/security/incidents/{id}");
        await team.GetAsync($"/api/security/incidents/{id}");
        (await team.PutAsJsonAsync($"/api/security/incidents/{id}", new { title = title + " (edited)", description = "Changed", category = "Phishing", severity = 2, personalDataInvolved = true })).EnsureSuccessStatusCode();

        var log = (await team.GetFromJsonAsync<List<AccessRow>>($"/api/security/incidents/{id}/access-log"))!;
        Assert.Equal(2, log.Count);
        await using var context = _api.CreateContext();
        Assert.False(await context.SystemAuditLogs.AnyAsync(l => l.EntityType.StartsWith("Security") || (l.EntityName != null && l.EntityName.Contains(reference))));
        Assert.True(await context.SecurityIncidentActions.AnyAsync(a => a.IncidentId == id && a.Field == "Severity" && a.NewValue == "2"));
    }

    [Fact]
    public async Task The_security_team_is_told_about_a_new_report_without_its_details()
    {
        var teamUser = await _api.CreateUserAsync(Manage);
        var (id, reference, title) = await ReportAsync(await _api.LoginAsNewUserAsync());

        await using var context = _api.CreateContext();
        var notification = await context.Notifications.SingleAsync(n => n.UserId == teamUser.Id && n.EntityType == "SecurityIncident" && n.EntityId == id);
        Assert.Contains(reference, notification.Body);
        Assert.DoesNotContain(title, notification.Body);
    }

    [Fact]
    public async Task A_revoked_grant_stops_working_without_logging_in_again()
    {
        var viewer = await _api.CreateUserAsync(View);
        var client = await _api.LoginAsync(viewer);
        var (id, _, _) = await ReportAsync(await _api.LoginAsNewUserAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/security/incidents/{id}")).StatusCode);

        await using (var context = _api.CreateContext())
        {
            var permission = await context.Permissions.SingleAsync(p => p.Key == View);
            context.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = viewer.Id, PermissionId = permission.Id, IsGranted = false });
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/security/incidents/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_serious_incident_cannot_be_closed_without_a_review()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var (id, _, _) = await ReportAsync(await _api.LoginAsNewUserAsync());
        (await team.PostAsJsonAsync($"/api/security/incidents/{id}/triage", new { outcome = "Incident", reason = "Credentials entered on fake page", category = "AccountCompromise", severity = 1 })).EnsureSuccessStatusCode();

        var refused = await team.PostAsJsonAsync($"/api/security/incidents/{id}/status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        (await team.PutAsJsonAsync($"/api/security/incidents/{id}/review", new { rootCause = "No MFA on the account", improvements = "Enforce MFA for all staff" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await team.PostAsJsonAsync($"/api/security/incidents/{id}/status", new { status = "Closed" })).StatusCode);

        var detail = (await team.GetFromJsonAsync<Detail>($"/api/security/incidents/{id}"))!;
        Assert.Equal("Closed", detail.Status);
        Assert.NotNull(detail.ContainedAt);
        Assert.NotNull(detail.ClosedAt);
    }

    private sealed record MyReport(string Reference, string Title, string Progress);
    private sealed record ListItem(int Id, string Reference);
    private sealed record Item(int Id, string Name, string Status);
    private sealed record Detail(int Id, string Status, bool CanManage, DateTime? ContainedAt, DateTime? ClosedAt, List<Item> AffectedItems);
    private sealed record AccessRow(int UserId, DateTime OpenedAt);
}
