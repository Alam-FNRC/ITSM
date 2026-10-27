using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ItsTool.API.HostedServices;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Infrastructure.BackgroundServices;
using ItsTool.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Starts the real API (middleware, JWT auth, permission policies, controllers) in memory against its own
/// throwaway SQL Server database. Users are logged in through the real /api/auth/login endpoint, so the
/// tokens carry exactly the roles and permissions the API itself would issue.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Password = "Integration-Test-Password-1";

    private readonly SqlServerFixture _database = new();
    private ItsmApiFactory? _factory;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public ItsToolDbContext CreateContext() => _database.CreateContext();

    /// <summary>Where this API instance stores uploaded attachments.</summary>
    public string UploadsPath => _factory?.UploadsPath ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new ItsmApiFactory(_database.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_factory != null) await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>Creates an active user holding exactly these permissions, through a role of its own.</summary>
    public Task<User> CreateUserAsync(params string[] permissions) =>
        CreateUserWithRoleAsync(TestData.Unique("Role"), permissions);

    public async Task<User> CreateUserWithRoleAsync(string roleName, params string[] permissions)
    {
        await using var context = CreateContext();

        var role = new Role { Name = roleName };
        context.Roles.Add(role);
        foreach (var key in permissions.Distinct())
        {
            var permission = await context.Permissions.FirstOrDefaultAsync(p => p.Key == key)
                ?? context.Permissions.Add(new Permission { Key = key, Name = key }).Entity;
            context.RolePermissions.Add(new RolePermission { Role = role, Permission = permission });
        }

        var username = TestData.Unique("user");
        var user = new User
        {
            Username = username,
            Email = $"{username}@fnrc.test",
            FirstName = "Test",
            LastName = "User",
            // Work factor 4 keeps the tests fast; the login code verifies any work factor.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 4)
        };
        context.Users.Add(user);
        context.UserRoles.Add(new UserRole { User = user, Role = role });
        await context.SaveChangesAsync();
        return user;
    }

    /// <summary>Logs the user in through the API and returns a client that sends their token.</summary>
    public async Task<HttpClient> LoginAsync(User user)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = Password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    public async Task<HttpClient> LoginAsNewUserAsync(params string[] permissions) =>
        await LoginAsync(await CreateUserAsync(permissions));

    private sealed record LoginResponse(string Token);
}

/// <summary>The API on a given database, as in production apart from a random JWT key and no background pollers.</summary>
internal sealed class ItsmApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ItsmApiFactory(string connectionString) => _connectionString = connectionString;

    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), $"itsm-test-uploads-{Guid.NewGuid():N}");

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(UploadsPath)) Directory.Delete(UploadsPath, recursive: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development, so no Swagger and no developer exception page, as in production. AutoSeed is off unless a test sets it.
        builder.UseEnvironment("Testing");
        // UseSetting rather than ConfigureAppConfiguration: Program.cs reads these before the app is built.
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Jwt:Secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("FileStorage:BasePath", UploadsPath);
        // Every test request comes from the same address, so the per-IP login limit would trip across tests
        builder.UseSetting("Auth:LoginAttemptsPerMinute", "100000");

        builder.ConfigureTestServices(services =>
        {
            // The SLA checker and e-mail sender poll the database in the background; tests drive the API directly.
            var pollers = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            (d.ImplementationType == typeof(SlaCheckerService) || d.ImplementationType == typeof(EmailBackgroundService)))
                .ToList();
            foreach (var poller in pollers) services.Remove(poller);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "API";
}
