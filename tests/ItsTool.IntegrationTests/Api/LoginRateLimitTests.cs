using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

namespace ItsTool.IntegrationTests.Api;

/// <summary>Login attempts from one address are limited per minute, whichever accounts they target (review A9).</summary>
[Collection(ApiCollection.Name)]
public class LoginRateLimitTests
{
    private readonly ApiFixture _api;

    public LoginRateLimitTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Attempts_beyond_the_limit_are_refused_with_429()
    {
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:LoginAttemptsPerMinute", "3"));
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { username = TestData.Unique("spray"), password = "Password1!" });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }, statuses);
    }

    [Fact]
    public async Task Other_endpoints_are_not_limited()
    {
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:LoginAttemptsPerMinute", "1"));
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        }
    }
}
