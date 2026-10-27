using System.Net;
using System.Net.Http.Json;
using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Api;

/// <summary>
/// Login matches a username or e-mail exactly, and each identifies one current user (review A9). Before, login
/// used SQL LIKE, so _ and % in the username acted as wildcards, and nothing stopped duplicate usernames.
/// </summary>
[Collection(ApiCollection.Name)]
public class UserIdentityTests
{
    private readonly ApiFixture _api;

    public UserIdentityTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Wildcards_in_the_username_do_not_match_other_users()
    {
        var user = await _api.CreateUserAsync("ticket.view");
        var pattern = user.Username[..^1] + "_";

        var response = await LoginAsync(pattern);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_log_in_with_their_username_in_any_case_or_with_their_email()
    {
        var user = await _api.CreateUserAsync("ticket.view");

        var upperCase = await LoginAsync(user.Username.ToUpperInvariant());
        var byEmail = await LoginAsync(user.Email);

        Assert.Equal(HttpStatusCode.OK, upperCase.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
    }

    [Fact]
    public async Task Creating_a_user_with_a_taken_username_or_email_is_refused()
    {
        var existing = await _api.CreateUserAsync("ticket.view");
        var admin = await _api.LoginAsNewUserAsync("admin.manage");

        var sameUsername = await admin.PostAsJsonAsync("/api/users", NewUser(existing.Username, TestData.Unique("new") + "@fnrc.test"));
        var sameEmail = await admin.PostAsJsonAsync("/api/users", NewUser(TestData.Unique("new"), existing.Email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, sameUsername.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
    }

    [Fact]
    public async Task The_database_refuses_duplicates_but_a_deleted_users_name_can_be_reused()
    {
        var existing = await _api.CreateUserAsync("ticket.view");
        await using var context = _api.CreateContext();

        context.Users.Add(new User { Username = existing.Username, Email = TestData.Unique("dup") + "@fnrc.test", PasswordHash = "x" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var departed = await context.Users.SingleAsync(u => u.Id == existing.Id);
        departed.IsDeleted = true;
        context.Users.Add(new User { Username = existing.Username, Email = existing.Email, PasswordHash = "x" });
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> LoginAsync(string username) =>
        _api.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login", new { username, password = ApiFixture.Password });

    private static object NewUser(string username, string email) => new
    {
        username,
        email,
        firstName = "New",
        lastName = "User",
        password = "Initial-Password-123",
        departmentId = (int?)null,
        profilePhoto = (string?)null,
        groupIds = Array.Empty<int>()
    };
}
