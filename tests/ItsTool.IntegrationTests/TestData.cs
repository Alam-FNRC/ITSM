using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Project;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Data;

namespace ItsTool.IntegrationTests;

/// <summary>
/// Builds the minimum rows a test needs. All tests share one database, so every name is made unique.
/// </summary>
internal static class TestData
{
    public static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    public static async Task<User> CreateUserAsync(ItsToolDbContext context)
    {
        var username = Unique("user");
        var user = new User
        {
            Username = username,
            Email = $"{username}@fnrc.test",
            FirstName = "Test",
            LastName = "User",
            PasswordHash = "not-a-real-hash"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public static async Task<TicketLookups> CreateTicketLookupsAsync(ItsToolDbContext context)
    {
        var project = new Project { Name = Unique("Project"), ProjectKey = Unique("P").ToUpperInvariant() };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var category = new Category { Name = "Hardware", ProjectId = project.Id };
        var type = new TicketType { Name = "Incident", ProjectId = project.Id };
        var status = new Status { Name = "New", ProjectId = project.Id, IsSystemDefault = true };
        var priority = new Priority { Name = "High", ProjectId = project.Id };
        context.AddRange(category, type, status, priority);
        await context.SaveChangesAsync();

        var requester = await CreateUserAsync(context);
        return new TicketLookups(project, category, type, status, priority, requester);
    }

    public static async Task<Ticket> CreateTicketAsync(ItsToolDbContext context, string title, string description)
    {
        var lookups = await CreateTicketLookupsAsync(context);
        var ticket = new Ticket
        {
            TicketNumber = Unique("T").ToUpperInvariant(),
            Title = title,
            Description = description,
            ProjectId = lookups.Project.Id,
            CategoryId = lookups.Category.Id,
            TypeId = lookups.Type.Id,
            StatusId = lookups.Status.Id,
            PriorityId = lookups.Priority.Id,
            RequesterUserId = lookups.Requester.Id
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();
        return ticket;
    }
}

internal sealed record TicketLookups(
    Project Project, Category Category, TicketType Type, Status Status, Priority Priority, User Requester);
