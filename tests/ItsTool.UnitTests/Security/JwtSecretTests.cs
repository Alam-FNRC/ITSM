using ItsTool.API.Security;
using Xunit;

namespace ItsTool.UnitTests.Security;

public class JwtSecretTests
{
    private const string GoodSecret = "k3Y-for-tests-only-0123456789-abcdefghij";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_secret_stops_startup_outside_development(string? configured)
    {
        var error = Assert.Throws<InvalidOperationException>(() => JwtSecret.Resolve(configured, isDevelopment: false));

        Assert.Contains("not configured", error.Message);
    }

    [Fact]
    public void Development_without_a_secret_gets_a_random_key_each_run()
    {
        var first = JwtSecret.Resolve(null, isDevelopment: true);
        var second = JwtSecret.Resolve(null, isDevelopment: true);

        Assert.True(first.Length >= JwtSecret.MinimumLength);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("PLACEHOLDER_SECRET_USE_USER_SECRETS", false)]
    [InlineData("fallback_secret_for_development_purposes_only", true)]
    [InlineData("enterprise_super_secret_jwt_key_turkcell_itsm_2026!", false)]
    public void Secrets_published_in_the_repository_are_refused_everywhere(string published, bool isDevelopment)
    {
        var error = Assert.Throws<InvalidOperationException>(() => JwtSecret.Resolve(published, isDevelopment));

        Assert.Contains("published", error.Message);
    }

    [Fact]
    public void A_short_secret_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => JwtSecret.Resolve("too-short-0123456789", isDevelopment: false));
    }

    [Fact]
    public void A_configured_secret_is_used_as_is()
    {
        Assert.Equal(GoodSecret, JwtSecret.Resolve(GoodSecret, isDevelopment: false));
    }
}
