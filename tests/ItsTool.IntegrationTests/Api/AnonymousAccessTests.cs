using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Only logging in and the health check work without a login. The debug and seed endpoints are gone, and
/// e-mail ingest, which creates users and tickets, is for administrators only (review A5).
/// </summary>
[Collection(ApiCollection.Name)]
public class AnonymousAccessTests
{
    private readonly ApiFixture _api;

    public AnonymousAccessTests(ApiFixture api) => _api = api;

    [Fact]
    public void Only_login_and_health_allow_anonymous_access()
    {
        var anonymous = _api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() != null)
            .Select(e => e.RoutePattern.RawText)
            .OrderBy(route => route)
            .ToList();

        Assert.Equal(new[] { "/api/health", "api/Auth/login" }, anonymous);
    }

    [Theory]
    [InlineData("POST", "/api/system/seed")]
    [InlineData("GET", "/api/system/db-test")]
    [InlineData("POST", "/api/system/ingest-email")]
    [InlineData("GET", "/api/test/roles")]
    [InlineData("GET", "/api/no-such-endpoint")]
    public async Task Formerly_open_endpoints_need_a_login(string method, string path)
    {
        var response = await _api.CreateAnonymousClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/system/seed")]
    [InlineData("GET", "/api/system/db-test")]
    [InlineData("GET", "/api/test/roles")]
    public async Task Debug_and_seed_endpoints_are_removed(string method, string path)
    {
        var client = await _api.LoginAsNewUserAsync("admin.manage");

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Email_ingest_is_for_administrators_only()
    {
        var client = await _api.LoginAsNewUserAsync("ticket.create", "ticket.view");

        var response = await client.PostAsJsonAsync("/api/system/ingest-email",
            new { messageId = TestData.Unique("msg"), from = "outsider@example.com", subject = "Hello", body = "Make me a user" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_health_check_works_without_a_login()
    {
        var response = await _api.CreateAnonymousClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
