using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Security;
using ItsTool.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ItsTool.UnitTests.Services;

public class SecurityIncidentServiceTests : TestBase
{
    private const int Reporter = 1, Team = 2;
    private readonly SecurityIncidentService _service;

    public SecurityIncidentServiceTests()
    {
        var permissions = new Mock<IPermissionCalculator>();
        permissions.Setup(p => p.CalculateEffectivePermissionsAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) => userId == Team
                ? new HashSet<string> { PermissionConstants.SecurityIncidentView, PermissionConstants.SecurityIncidentManage }
                : new HashSet<string>());
        _service = new SecurityIncidentService(_context, permissions.Object);
    }

    private async Task<int> ReportAsync(string title = "Odd pop-up")
    {
        var report = await _service.ReportAsync(Reporter, new ReportSecurityIssueDto(title, "Asked for my password", null));
        return await _context.SecurityIncidents.Where(i => i.Reference == report.Reference).Select(i => i.Id).SingleAsync();
    }

    private async Task<int> IncidentAsync(int severity = 3)
    {
        var id = await ReportAsync();
        await _service.TriageAsync(Team, id, new TriageSecurityIncidentDto("Incident", "Confirmed", "Malware", severity));
        return id;
    }

    private Task<SecurityIncident> LoadAsync(int id) => _context.SecurityIncidents.AsNoTracking().SingleAsync(i => i.Id == id);

    [Fact]
    public async Task References_are_numbered_per_year()
    {
        var first = await _service.ReportAsync(Reporter, new ReportSecurityIssueDto("One", "x", null));
        var second = await _service.ReportAsync(Reporter, new ReportSecurityIssueDto("Two", "x", null));

        Assert.Equal($"SEC-{DateTime.UtcNow.Year}-0001", first.Reference);
        Assert.Equal($"SEC-{DateTime.UtcNow.Year}-0002", second.Reference);
    }

    [Fact]
    public async Task A_report_that_is_not_a_security_issue_is_closed_with_its_reason()
    {
        var id = await ReportAsync();

        await _service.TriageAsync(Team, id, new TriageSecurityIncidentDto("NotSecurity", "Printer driver pop-up", null, null));

        var incident = await LoadAsync(id);
        Assert.Equal((SecurityIncidentStatus.Closed, SecurityTriageOutcome.NotSecurity, Team), (incident.Status, incident.TriageOutcome, incident.TriagedByUserId));
        Assert.NotNull(incident.ClosedAt);
        Assert.True(await _context.SecurityIncidentActions.AnyAsync(a => a.IncidentId == id && a.Kind == "Triaged" && a.Note == "Printer driver pop-up"));
        Assert.Equal("Closed", (await _service.GetMyReportsAsync(Reporter)).Single().Progress);
    }

    [Fact]
    public async Task An_incident_needs_a_category_and_severity_and_goes_to_containment()
    {
        var id = await ReportAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.TriageAsync(Team, id, new TriageSecurityIncidentDto("Incident", "Real", null, null)));

        await _service.TriageAsync(Team, id, new TriageSecurityIncidentDto("Incident", "Real", "Phishing", 2));

        var incident = await LoadAsync(id);
        Assert.Equal((SecurityIncidentStatus.Containment, SecurityIncidentCategory.Phishing, (int?)2, (int?)Team), (incident.Status, incident.Category, incident.Severity, incident.HandlerUserId));
        Assert.NotNull(incident.ContainedAt);
        Assert.Equal("InProgress", (await _service.GetMyReportsAsync(Reporter)).Single().Progress);
    }

    [Fact]
    public async Task Only_incidents_go_through_the_response_phases()
    {
        var id = await ReportAsync();
        await _service.TriageAsync(Team, id, new TriageSecurityIncidentDto("Event", "Blocked by the filter", null, null));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Eradication", "x")));
    }

    [Fact]
    public async Task Moving_back_or_reopening_needs_a_reason()
    {
        var id = await IncidentAsync();
        await _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Recovery", null));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Containment", null)));
        await _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Containment", "Malware came back"));
        await _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Closed", null));
        Assert.NotNull((await LoadAsync(id)).ClosedAt);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Triage", null)));
        await _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Triage", "New evidence"));
        var reopened = await LoadAsync(id);
        Assert.Equal(SecurityIncidentStatus.Triage, reopened.Status);
        Assert.Null(reopened.ClosedAt);
        Assert.NotNull(reopened.RecoveredAt);
    }

    [Fact]
    public async Task Severity_1_and_2_need_a_complete_review_to_close()
    {
        var id = await IncidentAsync(severity: 2);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Closed", null)));

        await _service.SaveReviewAsync(Team, id, new SaveSecurityReviewDto("Unpatched VPN", null, null, null));
        Assert.Null((await LoadAsync(id)).ReviewCompletedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Closed", null)));

        await _service.SaveReviewAsync(Team, id, new SaveSecurityReviewDto("Unpatched VPN", "Fast isolation", "Monthly patch check", null));
        await _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Closed", null));
        Assert.Equal(SecurityIncidentStatus.Closed, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Low_severity_incidents_close_without_a_review()
    {
        var id = await IncidentAsync(severity: 4);
        await _service.ChangeStatusAsync(Team, id, new ChangeSecurityStatusDto("Closed", null));
        Assert.Equal(SecurityIncidentStatus.Closed, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task A_false_positive_or_accepted_risk_needs_a_reason()
    {
        var id = await IncidentAsync();
        await _service.AddAffectedItemsAsync(Team, id, new AddSecurityAffectedItemsDto(new() { new("Server", "web01"), new("Address", "10.0.0.5") }));
        var items = await _context.SecurityAffectedItems.Where(a => a.IncidentId == id).OrderBy(a => a.Id).ToListAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAffectedItemAsync(Team, id, items[0].Id, new UpdateSecurityAffectedItemDto("FalsePositive", " ")));
        await _service.UpdateAffectedItemAsync(Team, id, items[0].Id, new UpdateSecurityAffectedItemDto("AcceptedRisk", "Retired next month"));
        await _service.UpdateAffectedItemAsync(Team, id, items[1].Id, new UpdateSecurityAffectedItemDto("Fixed", null));

        var list = (await _service.ListAsync(Team, new SecurityIncidentFilterDto())).Single(i => i.Id == id);
        Assert.Equal((2, 0), (list.AffectedItems, list.OpenAffectedItems));
        Assert.Equal(2, await _context.SecurityIncidentActions.CountAsync(a => a.IncidentId == id && a.Kind == "AffectedItemUpdated"));
    }

    [Fact]
    public void Enum_values_are_taken_by_name_only()
    {
        Assert.Equal(SecurityIncidentStatus.Eradication, SecurityIncidentService.ParseEnum<SecurityIncidentStatus>(" eradication ", "Status"));
        Assert.Throws<ArgumentException>(() => SecurityIncidentService.ParseEnum<SecurityIncidentStatus>("3", "Status"));
        Assert.Throws<ArgumentException>(() => SecurityIncidentService.ParseEnum<SecurityIncidentStatus>("Deleted", "Status"));
        Assert.Throws<ArgumentException>(() => SecurityIncidentService.ParseEnum<SecurityIncidentStatus>(null, "Status"));
    }

    [Fact]
    public async Task New_reports_go_to_everyone_who_holds_manage_by_any_route_except_a_deny()
    {
        var manage = new Permission { Key = PermissionConstants.SecurityIncidentManage, Name = PermissionConstants.SecurityIncidentManage };
        var role = new Role { Name = "Security Team" };
        var group = new Group { Name = "SOC" };
        var viaRole = new User { Username = "role", Email = "r@x", FirstName = "R" };
        var viaGroup = new User { Username = "group", Email = "g@x", FirstName = "G" };
        var viaGrant = new User { Username = "grant", Email = "s@x", FirstName = "S" };
        var denied = new User { Username = "denied", Email = "d@x", FirstName = "D" };
        var inactive = new User { Username = "gone", Email = "i@x", FirstName = "I", IsActive = false };
        _context.AddRange(manage, role, group, viaRole, viaGroup, viaGrant, denied, inactive);
        await _context.SaveChangesAsync();
        var groupRole = new Role { Name = "SOC role" };
        _context.Roles.Add(groupRole);
        await _context.SaveChangesAsync();
        _context.AddRange(
            new RolePermission { RoleId = role.Id, PermissionId = manage.Id },
            new RolePermission { RoleId = groupRole.Id, PermissionId = manage.Id },
            new UserRole { UserId = viaRole.Id, RoleId = role.Id },
            new UserRole { UserId = denied.Id, RoleId = role.Id },
            new UserRole { UserId = inactive.Id, RoleId = role.Id },
            new GroupRole { GroupId = group.Id, RoleId = groupRole.Id },
            new GroupMember { GroupId = group.Id, UserId = viaGroup.Id },
            new UserPermissionOverride { UserId = viaGrant.Id, PermissionId = manage.Id, IsGranted = true },
            new UserPermissionOverride { UserId = denied.Id, PermissionId = manage.Id, IsGranted = false });
        await _context.SaveChangesAsync();

        await ReportAsync();

        var told = await _context.Notifications.Where(n => n.EntityType == "SecurityIncident").Select(n => n.UserId).OrderBy(i => i).ToListAsync();
        Assert.Equal(new[] { viaRole.Id, viaGroup.Id, viaGrant.Id }.OrderBy(i => i), told);
    }
}
