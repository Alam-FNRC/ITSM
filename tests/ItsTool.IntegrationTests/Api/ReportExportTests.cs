namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// The ticket CSV export lists the tickets the caller may see: all of them with report.view, otherwise the
/// caller's own. It read the user id from a claim the token does not have, so every export came back empty.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportExportTests
{
    private readonly ApiFixture _api;

    public ReportExportTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task A_report_viewer_exports_other_peoples_tickets()
    {
        var title = TestData.Unique("Export check ");
        await using (var context = _api.CreateContext())
        {
            await TestData.CreateTicketAsync(context, title, "Listed in the export");
        }
        var client = await _api.LoginAsNewUserAsync("report.view", "ticket.view");

        var csv = await client.GetStringAsync("/api/reports/tickets/csv");

        Assert.Contains(title, csv);
    }

    [Fact]
    public async Task A_requester_exports_only_their_own_tickets()
    {
        var requester = await _api.CreateUserAsync("ticket.create", "ticket.view");
        var mine = TestData.Unique("Mine ");
        var theirs = TestData.Unique("Theirs ");
        await using (var context = _api.CreateContext())
        {
            await TestData.CreateTicketAsync(context, mine, "Own ticket", requester.Id);
            await TestData.CreateTicketAsync(context, theirs, "Someone else's ticket");
        }
        var client = await _api.LoginAsync(requester);

        var csv = await client.GetStringAsync("/api/reports/tickets/csv");

        Assert.Contains(mine, csv);
        Assert.DoesNotContain(theirs, csv);
    }
}
