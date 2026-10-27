using Microsoft.AspNetCore.Hosting;

namespace ItsTool.IntegrationTests.Api;

/// <summary>Outside Development the API must not start with a missing or published signing key (review A7).</summary>
[Collection(ApiCollection.Name)]
public class JwtSecretStartupTests
{
    private readonly ApiFixture _api;

    public JwtSecretStartupTests(ApiFixture api) => _api = api;

    [Theory]
    [InlineData("")]
    [InlineData("PLACEHOLDER_SECRET_USE_USER_SECRETS")]
    [InlineData("enterprise_super_secret_jwt_key_turkcell_itsm_2026!")]
    public void Production_refuses_to_start_without_a_proper_secret(string secret)
    {
        using var factory = _api.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:Secret", secret);
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Jwt:Secret", error.ToString());
    }
}
