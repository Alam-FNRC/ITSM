using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// kpi.manage keeps the IT KPI register and sees the report; SuperAdmin holds it without a grant, since the figures
/// are not sensitive. An owner sees and enters their own KPIs only; everyone else gets "not found" or "forbidden".
/// </summary>
[Collection(ApiCollection.Name)]
public class KpiRegisterAccessTests
{
    private const string Manage = PermissionConstants.KpiManage;
    private readonly ApiFixture _api;

    public KpiRegisterAccessTests(ApiFixture api) => _api = api;

    private static async Task<int> CapabilityAsync(HttpClient manager)
    {
        var response = await manager.PostAsJsonAsync("/api/kpi-register/capabilities", new { number = Random.Shared.Next(100, 999), nameEn = TestData.Unique("Capability "), nameAr = "قدرة" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KpiCapabilityDto>())!.Id;
    }

    private static object Form(int capabilityId, string name, int? ownerUserId = null, string cycle = "Quarterly") => new
    {
        number = Random.Shared.Next(1000, 999_999), capabilityId, nameEn = name, nameAr = "مؤشر", unit = "service", resultType = "Number", direction = "Up", cycle, ownerUserId
    };

    private static async Task<KpiDetailDto> CreateAsync(HttpClient manager, int capabilityId, string name, int? ownerUserId = null)
    {
        var response = await manager.PostAsJsonAsync("/api/kpi-register", Form(capabilityId, name, ownerUserId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KpiDetailDto>())!;
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

    private static string TokenPayload(HttpClient client)
    {
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')));
    }

    [Fact]
    public async Task Staff_without_kpi_manage_see_nothing_while_a_SuperAdmin_keeps_the_register()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage); // also makes sure the permission exists, as the seeder would
        var capabilityId = await CapabilityAsync(manager);
        var kpi = await CreateAsync(manager, capabilityId, TestData.Unique("Dashboards "));

        var outsider = await _api.LoginAsNewUserAsync("ticket.view", "report.view", "admin.manage");
        Assert.Empty((await outsider.GetFromJsonAsync<KpiRegisterDto>("/api/kpi-register"))!.Kpis);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/kpi-register/{kpi.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync($"/api/kpi-register/{kpi.Id}/values/2025/1", new { value = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync("/api/kpi-register", Form(capabilityId, "Mine"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/kpi-register/report?from=2023&to=2026")).StatusCode);
        Assert.DoesNotContain("kpiOwner", TokenPayload(outsider));

        var superAdmin = await LoginAsSuperAdminAsync();
        var created = await CreateAsync(superAdmin, capabilityId, TestData.Unique("Solutions in use "));
        Assert.True(created.CanManage);
        Assert.Contains((await superAdmin.GetFromJsonAsync<KpiYearReportDto>("/api/kpi-register/report?from=2025&to=2026"))!.Kpis, k => k.Id == created.Id);
    }

    [Fact]
    public async Task An_owner_enters_values_for_their_own_kpis_only_and_their_token_says_so()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage);
        var capabilityId = await CapabilityAsync(manager);
        var ownerUser = await _api.CreateUserAsync("ticket.view");
        var owned = await CreateAsync(manager, capabilityId, TestData.Unique("Portal availability "), ownerUser.Id);
        var other = await CreateAsync(manager, capabilityId, TestData.Unique("Service channels "));
        var owner = await _api.LoginAsync(ownerUser);
        Assert.Contains("\"kpiOwner\":\"true\"", TokenPayload(owner));

        var register = (await owner.GetFromJsonAsync<KpiRegisterDto>("/api/kpi-register?year=2025"))!;
        Assert.Equal(new[] { owned.Id }, register.Kpis.Select(k => k.Id));
        Assert.False(register.CanManage);

        (await owner.PutAsJsonAsync($"/api/kpi-register/{owned.Id}/values/2025/2", new { value = 99.5, note = "Monitoring system" })).EnsureSuccessStatusCode();
        var detail = (await owner.GetFromJsonAsync<KpiDetailDto>($"/api/kpi-register/{owned.Id}"))!;
        Assert.Equal((99.5m, "Monitoring system", ownerUser.Id, false), (detail.Values[0].Value, detail.Values[0].Note, detail.Values[0].EnteredByUserId!.Value, detail.CanManage));

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/kpi-register/{owned.Id}", Form(capabilityId, "Renamed", ownerUser.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/kpi-register/{owned.Id}/targets/2025", new { value = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/kpi-register/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync($"/api/kpi-register/{other.Id}/values/2025/1", new { value = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/kpi-register/{owned.Id}/values/2099/1", new { value = 1 })).StatusCode);

        // The change reached the system audit log, as every data change does
        await using var context = _api.CreateContext();
        Assert.True(await context.SystemAuditLogs.AnyAsync(l => l.EntityType == "KpiValue"));
    }

    [Fact]
    public async Task Only_kpi_manage_imports_the_sheet_and_reminders_run_against_the_real_database()
    {
        var manager = await _api.LoginAsNewUserAsync(Manage);
        var ownerUser = await _api.CreateUserAsync();
        var number = 900_000 + Random.Shared.Next(99_000);
        MemoryStream Sheet() => TestWorkbook.Build(TestWorkbook.KpiHeadings
            .Concat(TestWorkbook.KpiRow(4, "7", "بوابة الخدمات الذاتية", number.ToString(), "نسبة إتاحة البوابة", result: "نسبة مئوية", unit: "وحدة مئوية", v2026: "0.99")), "Sheet1");
        static MultipartFormDataContent Upload(Stream workbook, string fileName = "kpis-list.xlsx") => new() { { new StreamContent(workbook), "file", fileName } };

        var outsider = await _api.LoginAsNewUserAsync("report.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsync("/api/kpi-register/import/preview", Upload(Sheet()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/kpi-register/import/preview", Upload(Sheet(), "kpis.csv"))).StatusCode);

        var preview = (await (await manager.PostAsync("/api/kpi-register/import/preview", Upload(Sheet()))).Content.ReadFromJsonAsync<KpiImportPreviewDto>())!;
        Assert.Equal("Portal availability", Assert.Single(preview.Rows).NameEn);
        Assert.Equal(1, (await (await manager.PostAsync("/api/kpi-register/import", Upload(Sheet()))).Content.ReadFromJsonAsync<KpiImportResultDto>())!.Imported);

        await using var context = _api.CreateContext();
        var kpi = await context.KpiDefinitions.Include(k => k.Values).SingleAsync(k => k.Number == number);
        Assert.Equal(99m, Assert.Single(kpi.Values).Value);

        // Last year's value is missing once the owner is set, so the first run reminds them, and only once
        kpi.OwnerUserId = ownerUser.Id;
        await context.SaveChangesAsync();
        using (var scope = _api.Factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IKpiRegisterService>();
            Assert.True(await service.SendRemindersAsync(DateTime.UtcNow) >= 1);
            await service.SendRemindersAsync(DateTime.UtcNow);
        }
        Assert.Equal(1, await context.Notifications.CountAsync(n => n.UserId == ownerUser.Id && n.Type == "kpi.due" && n.EntityId == kpi.Id));
    }
}
