using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ItsTool.API.Controllers;

[ApiController]
[Route("api/lookup")]
[Authorize]
public class LookupController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly IProjectService _projectService;
    private readonly IDepartmentService _departmentService;
    private readonly IServiceCatalogService _serviceCatalog;
    private readonly IGroupService _groupService;

    public LookupController(ICatalogService catalogService, IProjectService projectService, IDepartmentService departmentService, IServiceCatalogService serviceCatalog, IGroupService groupService)
    {
        _groupService = groupService;
        _catalogService = catalogService;
        _projectService = projectService;
        _departmentService = departmentService;
        _serviceCatalog = serviceCatalog;
    }

    [HttpGet]
    public async Task<IActionResult> GetLookups()
    {
        var projects = await _projectService.GetAllAsync();
        var categories = await _catalogService.GetCategoriesAsync(null);
        var ticketTypes = await _catalogService.GetTicketTypesAsync();
        var priorities = await _catalogService.GetPrioritiesAsync();
        var statuses = await _catalogService.GetStatusesAsync();
        var departments = await _departmentService.GetAllAsync();
        // The ticket form groups the categories by service
        var services = await _serviceCatalog.GetAllAsync(activeOnly: true);
        // Team names for the ticket lists and the board, which show who a ticket is assigned to; names only
        var groups = (await _groupService.GetAllAsync()).Select(g => new { g.Id, g.Name, g.IsActive });

        return Ok(new
        {
            projects,
            categories,
            ticketTypes,
            priorities,
            statuses,
            departments,
            services,
            groups
        });
    }
}
