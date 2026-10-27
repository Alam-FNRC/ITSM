using System.Linq;
using System.Threading.Tasks;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ItsTool.UnitTests.Data;

public class DataSeederTests : TestBase
{
    [Fact]
    public async Task SeedAsync_ShouldUpsertFullReopenMatrix_WhenCalledMultipleTimes()
    {
        var seeder = new DataSeeder(_context);
        // First run
        await seeder.SeedAsync();

        var workflow = await _context.Workflows.FirstOrDefaultAsync(w => w.Name == "Default Global Workflow");
        Assert.NotNull(workflow);

        var closedStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == "Kapatıldı");
        var resolvedStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == "Çözüldü");
        var inProgressStatus = await _context.Statuses.FirstOrDefaultAsync(s => s.Name == "Devam Ediyor");

        Assert.NotNull(closedStatus);
        Assert.NotNull(resolvedStatus);
        Assert.NotNull(inProgressStatus);

        // Verify reopened transitions exist
        var closedToProgress = await _context.WorkflowTransitions
            .AnyAsync(wt => wt.WorkflowId == workflow!.Id && wt.FromStatusId == closedStatus!.Id && wt.ToStatusId == inProgressStatus!.Id);
        
        var resolvedToProgress = await _context.WorkflowTransitions
            .AnyAsync(wt => wt.WorkflowId == workflow!.Id && wt.FromStatusId == resolvedStatus!.Id && wt.ToStatusId == inProgressStatus!.Id);

        Assert.True(closedToProgress, "Closed -> In Progress transition missing");
        Assert.True(resolvedToProgress, "Resolved -> In Progress transition missing");

        // Delete one to simulate drift/modification
        var transitionToRemove = await _context.WorkflowTransitions
            .FirstAsync(wt => wt.FromStatusId == closedStatus!.Id && wt.ToStatusId == inProgressStatus!.Id);
        _context.WorkflowTransitions.Remove(transitionToRemove);
        await _context.SaveChangesAsync();

        // Second run - should upsert the missing one
        await seeder.SeedAsync();

        var missingTransitionIsBack = await _context.WorkflowTransitions
            .AnyAsync(wt => wt.WorkflowId == workflow!.Id && wt.FromStatusId == closedStatus!.Id && wt.ToStatusId == inProgressStatus!.Id);

        Assert.True(missingTransitionIsBack, "Seeder failed to UPSERT the missing Reopen transition.");
    }

    private static readonly FirstAdminSettings FirstAdmin = new("fnrc.admin", "it.admin@fnrc.test", "Bootstrap-Password-123");

    [Fact]
    public async Task SeedAsync_CreatesNoAccounts_WithoutBootstrapSettings()
    {
        await new DataSeeder(_context).SeedAsync();

        Assert.False(await _context.Users.AnyAsync());
    }

    [Fact]
    public async Task SeedAsync_CreatesOnlyTheConfiguredFirstAdministrator()
    {
        await new DataSeeder(_context, FirstAdmin).SeedAsync();

        var admin = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).SingleAsync();
        Assert.Equal("fnrc.admin", admin.Username);
        Assert.True(admin.MustChangePassword);
        Assert.True(BCrypt.Net.BCrypt.Verify("Bootstrap-Password-123", admin.PasswordHash));
        Assert.Equal("SuperAdmin", Assert.Single(admin.UserRoles).Role!.Name);
    }

    [Fact]
    public async Task SeedAsync_AuditsTheRolesAndPermissionsItGrants()
    {
        await new DataSeeder(_context, FirstAdmin).SeedAsync();

        var admin = await _context.Users.SingleAsync();
        var logs = await _context.SystemAuditLogs.ToListAsync();
        Assert.Contains(logs, l => l.EntityType == "User" && l.EntityId == admin.Id.ToString() &&
                                   l.Action == "RoleAssigned" && l.NewValue == "SuperAdmin" && l.CreatedBy == "system");
        Assert.Equal(await _context.RolePermissions.CountAsync(),
            logs.Count(l => l.EntityType == "Role" && l.Action == "PermissionAdded"));

        // A second start grants nothing new, so it records nothing new
        await new DataSeeder(_context, FirstAdmin).SeedAsync();
        Assert.Equal(logs.Count, await _context.SystemAuditLogs.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_NeverResetsPasswordsOrAddsAnAdministratorOnceUsersExist()
    {
        await new DataSeeder(_context, FirstAdmin).SeedAsync();
        var admin = await _context.Users.SingleAsync();
        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Changed-At-First-Login-1");
        await _context.SaveChangesAsync();

        await new DataSeeder(_context, FirstAdmin with { Username = "second.admin" }).SeedAsync();

        var users = await _context.Users.ToListAsync();
        Assert.Single(users);
        Assert.True(BCrypt.Net.BCrypt.Verify("Changed-At-First-Login-1", users[0].PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_NeverGrantsRolesToExistingUsersOrGroups()
    {
        // The old seeder made users like this SuperAdmin and gave every role-less group the Agent role
        _context.Users.Add(new User { Username = "engineer.admin", Email = "berkay@example.com", PasswordHash = "x" });
        _context.Groups.Add(new Group { Name = "Facilities" });
        await _context.SaveChangesAsync();

        await new DataSeeder(_context).SeedAsync();

        Assert.False(await _context.UserRoles.AnyAsync());
        Assert.False(await _context.GroupRoles.AnyAsync());
    }

    [Fact]
    public void FirstAdminSettings_AreOptional_ButMustBeComplete()
    {
        static IConfiguration Config(params (string Key, string Value)[] values) => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => "Bootstrap:" + v.Key, v => (string?)v.Value))
            .Build();

        Assert.Null(FirstAdminSettings.FromConfiguration(Config()));
        Assert.Throws<InvalidOperationException>(() => FirstAdminSettings.FromConfiguration(
            Config(("AdminUsername", "fnrc.admin"), ("AdminPassword", "Bootstrap-Password-123"))));
        Assert.Throws<InvalidOperationException>(() => FirstAdminSettings.FromConfiguration(
            Config(("AdminUsername", "fnrc.admin"), ("AdminEmail", "it.admin@fnrc.test"), ("AdminPassword", "short"))));
        Assert.Equal(FirstAdmin, FirstAdminSettings.FromConfiguration(
            Config(("AdminUsername", " fnrc.admin "), ("AdminEmail", "it.admin@fnrc.test"), ("AdminPassword", "Bootstrap-Password-123"))));
    }
}
