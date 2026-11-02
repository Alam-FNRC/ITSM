using ItsTool.Application.DTOs;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Data;
using ItsTool.Infrastructure.Services;

namespace ItsTool.UnitTests.Services;

public class ServiceCatalogServiceTests : TestBase
{
    private readonly ServiceCatalogService _services;
    private readonly CatalogService _catalog;

    public ServiceCatalogServiceTests()
    {
        _services = new ServiceCatalogService(_context);
        _catalog = new CatalogService(new Repository<Category>(_context), new Repository<TicketType>(_context),
            new Repository<Status>(_context), new Repository<Priority>(_context), _context);
    }

    [Fact]
    public async Task Services_are_listed_by_family_order_and_name_with_their_categories_counted()
    {
        var email = await _services.CreateAsync(new SaveItServiceDto("Email", "Communication", null, null, 2));
        await _services.CreateAsync(new SaveItServiceDto("Phones", "Communication", null, null, 1));
        await _services.CreateAsync(new SaveItServiceDto("  Network  ", "  ", "  Wired and Wi-Fi ", null));
        _context.Categories.AddRange(new Category { Name = "Mailbox full", ServiceId = email.Id }, new Category { Name = "Old", ServiceId = email.Id, IsDeleted = true });
        await _context.SaveChangesAsync();

        var list = await _services.GetAllAsync();

        Assert.Equal(new[] { "Network", "Phones", "Email" }, list.Select(s => s.Name));
        Assert.Null(list[0].Family);
        Assert.Equal("Wired and Wi-Fi", list[0].Description);
        Assert.Equal(1, list.Single(s => s.Name == "Email").CategoryCount);
    }

    [Fact]
    public async Task A_taken_name_or_a_missing_owner_team_is_refused()
    {
        await _services.CreateAsync(new SaveItServiceDto("Email", null, null, null));

        await Assert.ThrowsAsync<ArgumentException>(() => _services.CreateAsync(new SaveItServiceDto("Email", null, null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => _services.CreateAsync(new SaveItServiceDto("Printing", null, null, 999)));
        await Assert.ThrowsAsync<ArgumentException>(() => _services.CreateAsync(new SaveItServiceDto("   ", null, null, null)));
    }

    [Fact]
    public async Task Inactive_services_stay_out_of_the_ticket_form()
    {
        var team = new Group { Name = "Desk" };
        _context.Groups.Add(team);
        await _context.SaveChangesAsync();
        var printing = await _services.CreateAsync(new SaveItServiceDto("Printing", null, null, team.Id));

        await _services.UpdateAsync(printing.Id, new SaveItServiceDto("Printing", null, null, team.Id, IsActive: false));

        Assert.Empty(await _services.GetAllAsync(activeOnly: true));
        Assert.False(Assert.Single(await _services.GetAllAsync()).IsActive);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _services.UpdateAsync(999, new SaveItServiceDto("X", null, null, null)));
    }

    [Fact]
    public async Task A_service_with_categories_cannot_be_removed()
    {
        var email = await _services.CreateAsync(new SaveItServiceDto("Email", null, null, null));
        var category = new Category { Name = "Mailbox full", ServiceId = email.Id };
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _services.DeleteAsync(email.Id));

        category.ServiceId = null;
        await _context.SaveChangesAsync();
        await _services.DeleteAsync(email.Id);
        Assert.Null(await _services.GetAsync(email.Id));
        Assert.True((await _context.ItServices.FindAsync(email.Id))!.IsDeleted);
    }

    [Fact]
    public async Task A_category_keeps_its_service_type_and_team_and_they_must_exist()
    {
        var email = await _services.CreateAsync(new SaveItServiceDto("Email", null, null, null));
        var request = new TicketType { Name = "Service Request" };
        var team = new Group { Name = "Desk" };
        _context.AddRange(request, team);
        await _context.SaveChangesAsync();

        var created = await _catalog.CreateCategoryAsync(new CreateCategoryDto("New mailbox", 0, null, null, team.Id, email.Id, request.Id));
        Assert.Equal((email.Id, request.Id, team.Id), (created.ServiceId, created.DefaultTypeId, created.DefaultAssigneeGroupId));

        await _catalog.UpdateCategoryAsync(created.Id, new UpdateCategoryDto("New mailbox", 0, null, null, null, true, null, null));
        var updated = await _catalog.GetCategoryByIdAsync(created.Id);
        Assert.Equal((null, null, null), (updated!.ServiceId, updated.DefaultTypeId, updated.DefaultAssigneeGroupId));

        await Assert.ThrowsAsync<ArgumentException>(() => _catalog.CreateCategoryAsync(new CreateCategoryDto("X", 0, null, null, null, ServiceId: 999)));
        await Assert.ThrowsAsync<ArgumentException>(() => _catalog.CreateCategoryAsync(new CreateCategoryDto("X", 0, null, null, null, DefaultTypeId: 999)));
        await Assert.ThrowsAsync<ArgumentException>(() => _catalog.UpdateCategoryAsync(created.Id, new UpdateCategoryDto("X", 0, null, null, 999, true)));
    }
}
