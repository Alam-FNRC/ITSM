using System.Threading.Tasks;
using ItsTool.Domain.Entities.Catalog;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Services;
using Xunit;

namespace ItsTool.UnitTests.Services;

public class AssignmentEngineTests : TestBase
{
    private readonly AssignmentEngine _engine;

    public AssignmentEngineTests() : base()
    {
        _engine = new AssignmentEngine(_context);
    }

    [Fact]
    public async Task AssignTicketAsync_ShouldAssign_WhenRuleMatches()
    {
        var rule1 = new AssignmentRule { Name = "R1", SortOrder = 10, ProjectId = 1, TargetUserId = 99, IsActive = true };
        _context.AssignmentRules.Add(rule1);
        await _context.SaveChangesAsync();

        var ticket = new Ticket { ProjectId = 1, CategoryId = 2 };
        
        await _engine.AssignTicketAsync(ticket);
        
        Assert.Equal(99, ticket.Assignments.FirstOrDefault()?.AssignedUserId);
    }

    [Fact]
    public async Task A_rule_assignment_is_recorded_in_the_ticket_history()
    {
        _context.AssignmentRules.Add(new AssignmentRule { Name = "Network", SortOrder = 1, TargetUserId = 99, TargetGroupId = 7, IsActive = true });
        await _context.SaveChangesAsync();
        var ticket = new Ticket { Id = 42, TicketNumber = "T-42" };

        await _engine.AssignTicketAsync(ticket);
        await _context.SaveChangesAsync();

        var history = Assert.Single(_context.TicketHistories);
        Assert.Equal((42, "Assigned", "Users:99,Groups:7", "System"), (history.TicketId, history.Action, history.NewValue, history.CreatedBy));
    }

    [Fact]
    public async Task AssignTicketAsync_ShouldSkipInactiveRules()
    {
        var rule1 = new AssignmentRule { Name = "R1", SortOrder = 10, ProjectId = 1, TargetUserId = 99, IsActive = false };
        _context.AssignmentRules.Add(rule1);
        await _context.SaveChangesAsync();

        var ticket = new Ticket { ProjectId = 1 };
        
        await _engine.AssignTicketAsync(ticket);
        
        Assert.Null(ticket.Assignments.FirstOrDefault()?.AssignedUserId); // Remained unassigned
    }

    [Fact]
    public async Task AssignTicketAsync_ShouldPickFirstMatchBySortOrder()
    {
        var rule1 = new AssignmentRule { Name = "R1", SortOrder = 20, ProjectId = 1, TargetUserId = 99, IsActive = true };
        var rule2 = new AssignmentRule { Name = "R2", SortOrder = 10, ProjectId = 1, TargetUserId = 88, IsActive = true };
        _context.AssignmentRules.AddRange(rule1, rule2);
        await _context.SaveChangesAsync();

        var ticket = new Ticket { ProjectId = 1 };
        
        await _engine.AssignTicketAsync(ticket);
        
        Assert.Equal(88, ticket.Assignments.FirstOrDefault()?.AssignedUserId); // 10 comes before 20
    }

    private async Task<(Group Network, Group Desk, Category Category)> CatalogueAsync(bool categoryTeam)
    {
        var network = new Group { Name = "Network" };
        var desk = new Group { Name = "Service desk" };
        var service = new ItService { Name = "Network and internet", OwnerGroup = network };
        var category = new Category { Name = "Wi-Fi", Service = service };
        _context.AddRange(network, desk, service, category);
        await _context.SaveChangesAsync();
        if (categoryTeam)
        {
            category.DefaultAssigneeGroupId = desk.Id;
            await _context.SaveChangesAsync();
        }
        return (network, desk, category);
    }

    [Fact]
    public async Task Without_a_matching_rule_the_service_owner_team_gets_the_ticket()
    {
        var (network, _, category) = await CatalogueAsync(categoryTeam: false);
        var ticket = new Ticket { Id = 5, CategoryId = category.Id };

        await _engine.AssignTicketAsync(ticket);
        await _context.SaveChangesAsync();

        Assert.Equal(network.Id, Assert.Single(ticket.Assignments).AssignedGroupId);
        Assert.Equal($"Users:,Groups:{network.Id}", Assert.Single(_context.TicketHistories).NewValue);
    }

    [Fact]
    public async Task The_category_team_comes_before_the_service_owner()
    {
        var (_, desk, category) = await CatalogueAsync(categoryTeam: true);
        var ticket = new Ticket { CategoryId = category.Id };

        await _engine.AssignTicketAsync(ticket);

        Assert.Equal(desk.Id, Assert.Single(ticket.Assignments).AssignedGroupId);
    }

    [Fact]
    public async Task A_matching_rule_comes_before_the_catalogue_and_a_removed_team_gets_nothing()
    {
        var (network, _, category) = await CatalogueAsync(categoryTeam: false);
        _context.AssignmentRules.Add(new AssignmentRule { Name = "VIP", SortOrder = 1, CategoryId = category.Id, TargetUserId = 77, IsActive = true });
        await _context.SaveChangesAsync();

        var ticket = new Ticket { CategoryId = category.Id };
        await _engine.AssignTicketAsync(ticket);
        Assert.Equal(77, Assert.Single(ticket.Assignments).AssignedUserId);

        _context.AssignmentRules.RemoveRange(_context.AssignmentRules);
        network.IsDeleted = true;
        await _context.SaveChangesAsync();
        var another = new Ticket { CategoryId = category.Id };
        await _engine.AssignTicketAsync(another);
        Assert.Empty(another.Assignments);
    }
}
