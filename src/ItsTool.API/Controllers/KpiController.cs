using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ItsTool.API.Controllers;

/// <summary>Service desk KPIs over every ticket, so only report.view holders get them.</summary>
[ApiController]
[Route("api/reports/kpi")]
[Authorize(Policy = "RequirePermission:report.view")]
public class KpiController : ControllerBase
{
    private readonly IKpiService _kpiService;

    public KpiController(IKpiService kpiService) => _kpiService = kpiService;

    [HttpGet]
    [ProducesResponseType(typeof(KpiReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReport([FromQuery] KpiFilterDto filter)
    {
        try
        {
            return Ok(await _kpiService.GetReportAsync(filter));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("filters")]
    [ProducesResponseType(typeof(KpiFilterOptionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilterOptions() => Ok(await _kpiService.GetFilterOptionsAsync());
}
