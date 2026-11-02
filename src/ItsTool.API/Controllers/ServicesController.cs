using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ItsTool.API.Controllers;

/// <summary>
/// Maintains the service catalogue, like the categories screen. Everyone who logs in reads the active services
/// through /api/lookup for the ticket form.
/// </summary>
[ApiController]
[Route("api/services")]
[Authorize(Policy = "RequirePermission:admin.manage")]
public class ServicesController : ControllerBase
{
    private readonly IServiceCatalogService _services;

    public ServicesController(IServiceCatalogService services) => _services = services;

    [HttpGet]
    [ProducesResponseType(typeof(List<ItServiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll() => Ok(await _services.GetAllAsync());

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ItServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id)
    {
        var service = await _services.GetAsync(id);
        return service == null ? NotFound() : Ok(service);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ItServiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] SaveItServiceDto dto)
    {
        try
        {
            var created = await _services.CreateAsync(dto);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveItServiceDto dto)
    {
        try
        {
            await _services.UpdateAsync(id, dto);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _services.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
