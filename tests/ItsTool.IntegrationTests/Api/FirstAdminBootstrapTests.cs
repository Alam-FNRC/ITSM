using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// A new installation gets no demo accounts; its only account is the administrator named in the Bootstrap
/// settings, who must change the password at first login (review A8). Uses its own empty database.
/// </summary>
public class FirstAdminBootstrapTests : IAsyncLifetime
{
    private const string Password = "Bootstrap-Password-123";

    private readonly SqlServerFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task A_new_installation_has_only_the_configured_administrator()
    {
        await using var factory = new ItsmApiFactory(_database.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AutoSeed", "true");
            builder.UseSetting("Bootstrap:AdminUsername", "fnrc.admin");
            builder.UseSetting("Bootstrap:AdminEmail", "it.admin@fnrc.test");
            builder.UseSetting("Bootstrap:AdminPassword", Password);
        });
        var client = factory.CreateClient();

        var oldDemoLogin = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin123!" });
        var adminLogin = await client.PostAsJsonAsync("/api/auth/login", new { username = "fnrc.admin", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, oldDemoLogin.StatusCode);
        adminLogin.EnsureSuccessStatusCode();
        var admin = await adminLogin.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(admin!.MustChangePassword);
        Assert.Contains("SuperAdmin", admin.Roles);
        await using var context = _database.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync());
    }

    private sealed record LoginResponse(bool MustChangePassword, List<string> Roles);
}
