using System.Net;
using System.Net.Http.Json;
using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// The risk register names FNRC's weak spots, so only the security team (risk.manage) and approvers (risk.approve)
/// see it, plus each owner their own risks: not other staff, not auditors, not even SuperAdmin without a grant.
/// Everyone else gets "not found", and nothing about a risk reaches the system audit log.
/// </summary>
[Collection(ApiCollection.Name)]
public class RiskAccessTests
{
    private const string Manage = PermissionConstants.RiskManage;
    private const string Approve = PermissionConstants.RiskApprove;
    private readonly ApiFixture _api;

    public RiskAccessTests(ApiFixture api) => _api = api;

    private static object Form(string title, int? ownerUserId = null, string? targetDate = null) => new
    {
        title, threat = "Outage", impactConfidentiality = 3, impactIntegrity = 5, impactAvailability = 5, likelihood = 4,
        treatment = "Mitigate", annexAControls = new[] { "8.13" }, ownerUserId, targetDate
    };

    private static async Task<RiskDetailDto> CreateAsync(HttpClient manager, string title, int? ownerUserId = null, string? targetDate = null)
    {
        var response = await manager.PostAsJsonAsync("/api/risks", Form(title, ownerUserId, targetDate));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RiskDetailDto>())!;
    }

    private async Task<HttpClient> LoginAsSuperAdminAsync()
    {
        var user = await _api.CreateUserAsync();
        await using (var context = _api.CreateContext())
        {
            var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == "SuperAdmin") ?? context.Roles.Add(new Role { Name = "SuperAdmin" }).Entity;
            context.UserRoles.Add(new UserRole { UserId = user.Id, Role = role });
            await context.SaveChangesAsync();
        }
        return await _api.LoginAsync(user);
    }

    [Fact]
    public async Task Staff_auditors_and_a_SuperAdmin_without_a_grant_cannot_see_the_register()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage);
        var risk = await CreateAsync(manager, TestData.Unique("Single point of failure "));
        Assert.Equal(17.33, risk.Score);

        foreach (var outsider in new[] { await _api.LoginAsNewUserAsync("ticket.view", "report.view", "audit.view", "admin.manage"), await LoginAsSuperAdminAsync() })
        {
            Assert.Empty((await outsider.GetFromJsonAsync<RiskRegisterDto>("/api/risks"))!.Risks);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/risks/{risk.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync($"/api/risks/{risk.Id}", Form("Renamed"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync("/api/risks", Form("Mine"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync("/api/risks/approve", new { ids = new[] { risk.Id } })).StatusCode);
        }

        await using var context = _api.CreateContext();
        Assert.False(await context.SystemAuditLogs.AnyAsync(l => l.EntityType.StartsWith("Risk")));
    }

    [Fact]
    public async Task The_approver_approves_with_one_click_but_does_not_edit()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage);
        var approver = await _api.LoginAsNewUserAsync(Approve);
        var risk = await CreateAsync(manager, TestData.Unique("No offline backup "));

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/risks/approve", new { ids = new[] { risk.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PutAsJsonAsync($"/api/risks/{risk.Id}", Form("Renamed"))).StatusCode);

        var approved = await approver.PostAsJsonAsync("/api/risks/approve", new { ids = new[] { risk.Id } });
        Assert.Equal(1, (await approved.Content.ReadFromJsonAsync<RiskBulkResultDto>())!.Changed);
        var detail = (await approver.GetFromJsonAsync<RiskDetailDto>($"/api/risks/{risk.Id}"))!;
        Assert.NotNull(detail.ApprovedAt);
        Assert.False(detail.CanManage);

        // A new target date changes the plan, so it needs approving again
        (await manager.PutAsJsonAsync($"/api/risks/{risk.Id}", Form(risk.Title, targetDate: "2027-03-31"))).EnsureSuccessStatusCode();
        Assert.Null((await manager.GetFromJsonAsync<RiskDetailDto>($"/api/risks/{risk.Id}"))!.ApprovedAt);
    }

    [Fact]
    public async Task An_owner_sees_their_own_risks_read_only()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage);
        var ownerUser = await _api.CreateUserAsync("ticket.view");
        var owner = await _api.LoginAsync(ownerUser);
        var owned = await CreateAsync(manager, TestData.Unique("Weak passwords "), ownerUser.Id);
        var other = await CreateAsync(manager, TestData.Unique("Lost laptop "));

        var register = (await owner.GetFromJsonAsync<RiskRegisterDto>("/api/risks"))!;
        Assert.Equal(new[] { owned.Id }, register.Risks.Select(r => r.Id));
        Assert.False((await owner.GetFromJsonAsync<RiskDetailDto>($"/api/risks/{owned.Id}"))!.CanManage);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/risks/{owned.Id}", Form("Renamed", ownerUser.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/risks/{other.Id}")).StatusCode);
    }

    private static MultipartFormDataContent Upload(Stream workbook, string fileName = "register.xlsx", string? owners = null)
    {
        var form = new MultipartFormDataContent { { new StreamContent(workbook), "file", fileName } };
        if (owners != null) form.Add(new StringContent(owners), "owners");
        return form;
    }

    [Fact]
    public async Task Only_the_security_team_imports_the_excel_register()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage);
        var approver = await _api.LoginAsNewUserAsync(Approve);
        var ownerUser = await _api.CreateUserAsync();
        // Numbers far above the ones other tests create
        var number = 900_000 + Random.Shared.Next(99_000);
        MemoryStream Register() => TestWorkbook.Build(TestWorkbook.RegisterHeadings
            .Concat(TestWorkbook.RegisterRow(5, number.ToString(), "Imported risk", "3", "3", "4", "4", annex: "A.5.23", owner: "Head of IT Section", date: "Q2 2026", status: "Planned")));

        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostAsync("/api/risks/import/preview", Upload(Register()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/risks/import/preview", Upload(Register(), "register.csv"))).StatusCode);

        var preview = (await (await manager.PostAsync("/api/risks/import/preview", Upload(Register()))).Content.ReadFromJsonAsync<RiskImportPreviewDto>())!;
        Assert.Equal(new[] { "Head of IT Section" }, preview.Owners);

        var import = await manager.PostAsync("/api/risks/import", Upload(Register(), owners: $"{{\"Head of IT Section\": {ownerUser.Id}}}"));
        Assert.Equal(1, (await import.Content.ReadFromJsonAsync<RiskImportResultDto>())!.Imported);

        await using var context = _api.CreateContext();
        var risk = await context.Risks.SingleAsync(r => r.Number == number);
        Assert.Equal((ownerUser.Id, new DateOnly(2026, 6, 30), "5.23"), (risk.OwnerUserId!.Value, risk.TargetDate!.Value, risk.AnnexAControls));
    }

    [Fact]
    public async Task Reminders_run_against_the_real_database()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage);
        var ownerUser = await _api.CreateUserAsync();
        var risk = await CreateAsync(manager, TestData.Unique("Unpatched servers "), ownerUser.Id, DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd"));

        using (var scope = _api.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRiskService>().SendRemindersAsync(DateTime.UtcNow);
        }

        await using var context = _api.CreateContext();
        Assert.True(await context.Notifications.AnyAsync(n => n.UserId == ownerUser.Id && n.Type == "risk.overdue" && n.EntityId == risk.Id));
    }
}
