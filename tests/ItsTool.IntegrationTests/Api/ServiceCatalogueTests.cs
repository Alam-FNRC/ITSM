using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ItsTool.Application.DTOs;
using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Admins keep the service catalogue; everyone who logs in sees the active services on the ticket form. A new ticket
/// in a category of a service goes to the service's owner team when no assignment rule applies.
/// </summary>
[Collection(ApiCollection.Name)]
public class ServiceCatalogueTests
{
    private readonly ApiFixture _api;

    public ServiceCatalogueTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Only_admins_can_maintain_the_catalogue()
    {
        var agent = await _api.LoginAsNewUserAsync("ticket.view", "ticket.edit", "report.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("/api/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("/api/services", new { name = TestData.Unique("Email") })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.DeleteAsync("/api/services/1")).StatusCode);
    }

    [Fact]
    public async Task A_new_ticket_in_a_service_goes_to_its_owner_team_and_shows_in_the_kpis()
    {
        var admin = await _api.LoginAsNewUserAsync("admin.manage");
        int teamId, projectId, typeId, priorityId;
        await using (var context = _api.CreateContext())
        {
            var lookups = await TestData.CreateTicketLookupsAsync(context);
            var team = new Group { Name = TestData.Unique("Messaging") };
            context.Groups.Add(team);
            await context.SaveChangesAsync();
            (teamId, projectId, typeId, priorityId) = (team.Id, lookups.Project.Id, lookups.Type.Id, lookups.Priority.Id);
        }

        var serviceResponse = await admin.PostAsJsonAsync("/api/services", new { name = TestData.Unique("Email"), family = "Communication", ownerGroupId = teamId });
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var service = (await serviceResponse.Content.ReadFromJsonAsync<ItServiceDto>())!;
        var categoryResponse = await admin.PostAsJsonAsync("/api/categories", new { name = "Mailbox full", projectId, serviceId = service.Id, defaultTypeId = typeId });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>())!;

        var requester = await _api.LoginAsNewUserAsync("ticket.create", "ticket.view");
        var lookup = await requester.GetFromJsonAsync<JsonElement>("/api/lookup");
        Assert.Contains(lookup.GetProperty("services").EnumerateArray(), s => s.GetProperty("id").GetInt32() == service.Id);
        // The board and the ticket list name the team a ticket is assigned to
        Assert.Contains(lookup.GetProperty("groups").EnumerateArray(), g => g.GetProperty("id").GetInt32() == teamId && g.GetProperty("name").GetString()!.StartsWith("Messaging"));

        var created = await requester.PostAsJsonAsync("/api/ticket", new
        {
            title = "Cannot send email", description = "Mailbox is full", projectId, categoryId = category.Id, typeId, priorityId,
            requesterUserId = 0, customFields = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ticket = (await created.Content.ReadFromJsonAsync<TicketDto>())!;

        await using (var check = _api.CreateContext())
        {
            var assignment = await check.TicketAssignments.SingleAsync(a => a.TicketId == ticket.Id);
            Assert.Equal(teamId, assignment.AssignedGroupId);
        }

        var reports = await _api.LoginAsNewUserAsync("report.view");
        var today = DateTime.UtcNow.Date;
        var kpis = await reports.GetFromJsonAsync<KpiReportDto>($"/api/reports/kpi?from={today.AddDays(-3):yyyy-MM-dd}&to={today.AddDays(3):yyyy-MM-dd}&serviceId={service.Id}");
        Assert.Equal(1, kpis!.Summary.Opened);
        Assert.Equal(service.Name, Assert.Single(kpis.Breakdowns["service"]).Name);
    }

    [Fact]
    public async Task A_service_that_still_has_categories_is_not_removed()
    {
        var admin = await _api.LoginAsNewUserAsync("admin.manage");
        var service = (await (await admin.PostAsJsonAsync("/api/services", new { name = TestData.Unique("Printing") })).Content.ReadFromJsonAsync<ItServiceDto>())!;
        var category = (await (await admin.PostAsJsonAsync("/api/categories", new { name = "Paper jam", projectId = 0, serviceId = service.Id })).Content.ReadFromJsonAsync<CategoryDto>())!;

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/services/{service.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/categories", new { name = "Toner", projectId = 0, serviceId = 999999 })).StatusCode);

        (await admin.PutAsJsonAsync($"/api/categories/{category.Id}", new { name = "Paper jam", projectId = 0, isActive = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/services/{service.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/services/{service.Id}")).StatusCode);
    }
}
