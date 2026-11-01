using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ItsTool.Application.Constants;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Evidence follows the incident's access rules: the security team and people on the incident add it, anyone who
/// can see the incident may download or check a file, only the security team records hand-overs, and everyone else
/// gets "not found". Files are fingerprinted, every copy is recorded, and a changed file is refused.
/// </summary>
[Collection(ApiCollection.Name)]
public class SecurityEvidenceAccessTests
{
    private const string View = PermissionConstants.SecurityIncidentView;
    private const string Manage = PermissionConstants.SecurityIncidentManage;
    private static readonly byte[] EventLog = Encoding.UTF8.GetBytes("4625 An account failed to log on: administrator from 10.0.0.66");
    private readonly ApiFixture _api;

    public SecurityEvidenceAccessTests(ApiFixture api) => _api = api;

    private async Task<int> IncidentAsync()
    {
        var reporter = await _api.LoginAsNewUserAsync();
        var response = await reporter.PostAsJsonAsync("/api/security/reports", new { title = TestData.Unique("Odd logons "), description = "Many failures", occurredAt = (DateTime?)null });
        response.EnsureSuccessStatusCode();
        var reference = (await response.Content.ReadFromJsonAsync<MyReport>())!.Reference;
        await using var context = _api.CreateContext();
        return await context.SecurityIncidents.Where(i => i.Reference == reference).Select(i => i.Id).SingleAsync();
    }

    private static MultipartFormDataContent Form(string kind, string description, byte[]? file = null, string fileName = "Security.log", string? location = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(kind), "kind" }, { new StringContent(description), "description" } };
        if (location != null) form.Add(new StringContent(location), "location");
        if (file != null) form.Add(new ByteArrayContent(file), "file", fileName);
        return form;
    }

    private static Task<HttpResponseMessage> UploadLogAsync(HttpClient client, int incidentId) =>
        client.PostAsync($"/api/security/incidents/{incidentId}/evidence", Form("LogExport", "Security log from fs01", EventLog));

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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
    public async Task The_security_team_keeps_a_file_and_every_copy_is_checked_and_recorded()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var id = await IncidentAsync();

        var upload = await UploadLogAsync(team, id);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var evidence = (await upload.Content.ReadFromJsonAsync<Evidence>())!;
        Assert.Equal((1, Sha256(EventLog)), (evidence.Number, evidence.Sha256));

        await using (var context = _api.CreateContext())
        {
            var path = await context.SecurityEvidence.Where(e => e.Id == evidence.Id).Select(e => e.FilePath).SingleAsync();
            Assert.StartsWith(Path.GetFullPath(_api.EvidencePath), path);
            Assert.Equal(EventLog, await File.ReadAllBytesAsync(path!));
        }

        var download = await team.GetAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(EventLog, await download.Content.ReadAsByteArrayAsync());
        // Always saved as a file, never opened in the browser
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));

        var detail = (await team.GetFromJsonAsync<Detail>($"/api/security/incidents/{id}"))!;
        Assert.Equal(new[] { "Collected", "Downloaded" }, detail.Evidence.Single().Custody.Select(c => c.Kind));
    }

    [Fact]
    public async Task Staff_and_a_SuperAdmin_without_a_grant_cannot_reach_evidence()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var id = await IncidentAsync();
        var evidence = (await (await UploadLogAsync(team, id)).Content.ReadFromJsonAsync<Evidence>())!;
        var colleague = await _api.LoginAsNewUserAsync("ticket.view", "ticket.manage", "report.view", "audit.view");
        var superAdmin = await _api.LoginAsync(await CreateSuperAdminAsync());

        foreach (var client in new[] { colleague, superAdmin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await UploadLogAsync(client, id)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}/download")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}/check", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}/hand-over",
                new { toUserId = 1, location = "Desk" })).StatusCode);
        }
    }

    [Fact]
    public async Task A_viewer_copies_and_checks_but_adds_nothing_and_a_member_adds_but_hands_nothing_over()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var id = await IncidentAsync();
        var viewer = await _api.LoginAsNewUserAsync(View);
        var memberUser = await _api.CreateUserAsync();
        var member = await _api.LoginAsync(memberUser);
        (await team.PostAsJsonAsync($"/api/security/incidents/{id}/members", new { userId = memberUser.Id })).EnsureSuccessStatusCode();

        var disk = await member.PostAsync($"/api/security/incidents/{id}/evidence", Form("Device", "fs01 system disk", location: "Server room rack 3"));
        Assert.Equal(HttpStatusCode.OK, disk.StatusCode);
        var diskId = (await disk.Content.ReadFromJsonAsync<Evidence>())!.Id;
        var logId = (await (await UploadLogAsync(member, id)).Content.ReadFromJsonAsync<Evidence>())!.Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadLogAsync(viewer, id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/security/incidents/{id}/evidence/{logId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsync($"/api/security/incidents/{id}/evidence/{logId}/check", null)).StatusCode);

        var handOver = new { toUserId = memberUser.Id, location = "Forensics lab", note = "Sealed bag 0042" };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/security/incidents/{id}/evidence/{diskId}/hand-over", handOver)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/security/incidents/{id}/evidence/{diskId}/hand-over", handOver)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await team.PostAsJsonAsync($"/api/security/incidents/{id}/evidence/{diskId}/hand-over", handOver)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await team.PostAsJsonAsync($"/api/security/incidents/{id}/evidence/{logId}/hand-over", handOver)).StatusCode);
    }

    [Fact]
    public async Task A_changed_file_is_refused_and_the_security_team_is_told()
    {
        var teamUser = await _api.CreateUserAsync(Manage);
        var team = await _api.LoginAsync(teamUser);
        var id = await IncidentAsync();
        var evidence = (await (await UploadLogAsync(team, id)).Content.ReadFromJsonAsync<Evidence>())!;
        await using (var context = _api.CreateContext())
        {
            var path = await context.SecurityEvidence.Where(e => e.Id == evidence.Id).Select(e => e.FilePath).SingleAsync();
            await File.WriteAllTextAsync(path!, "4624 An account was successfully logged on");
        }

        var download = await team.GetAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}/download");
        Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);
        var check = (await (await team.PostAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}/check", null)).Content.ReadFromJsonAsync<Check>())!;
        Assert.False(check.Intact);
        Assert.Equal(Sha256(EventLog), check.Expected);

        await using (var context = _api.CreateContext())
        {
            Assert.True(await context.Notifications.AnyAsync(n => n.UserId == teamUser.Id && n.Type == "security.evidence.failed" && n.EntityId == id));
            Assert.False(await context.SystemAuditLogs.AnyAsync(l => l.EntityType.StartsWith("Security")));
        }
    }

    [Fact]
    public async Task Evidence_cannot_be_deleted_or_replaced_through_the_API()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var id = await IncidentAsync();
        var evidence = (await (await UploadLogAsync(team, id)).Content.ReadFromJsonAsync<Evidence>())!;

        Assert.False((await team.DeleteAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}")).IsSuccessStatusCode);
        Assert.False((await team.PutAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}", Form("LogExport", "Swapped", Encoding.UTF8.GetBytes("other")))).IsSuccessStatusCode);

        var download = await team.GetAsync($"/api/security/incidents/{id}/evidence/{evidence.Id}/download");
        Assert.Equal(EventLog, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Files_over_25_MB_and_unknown_types_are_refused()
    {
        var team = await _api.LoginAsNewUserAsync(Manage);
        var id = await IncidentAsync();

        var tooBig = await team.PostAsync($"/api/security/incidents/{id}/evidence", Form("LogExport", "Huge export", new byte[25 * 1024 * 1024 + 1]));
        var program = await team.PostAsync($"/api/security/incidents/{id}/evidence", Form("Other", "Dropper", Encoding.ASCII.GetBytes("MZ"), "dropper.exe"));

        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, program.StatusCode);
        await using var context = _api.CreateContext();
        Assert.False(await context.SecurityEvidence.AnyAsync(e => e.IncidentId == id));
    }

    private sealed record MyReport(string Reference);
    private sealed record Custody(string Kind);
    private sealed record Evidence(int Id, int Number, string? Sha256, List<Custody> Custody);
    private sealed record Detail(List<Evidence> Evidence);
    private sealed record Check(bool Intact, string Expected, string? Found);
}
