using System.Security.Claims;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ItsTool.API.Controllers;

/// <summary>
/// The IT KPI register. Rights are checked per call by KpiRegisterService against the database: kpi.manage for the
/// definitions and the report, the KPI's owner for its values. A KPI the caller may not see is "not found".
/// </summary>
[ApiController]
[Route("api/kpi-register")]
[Authorize]
public class KpiRegisterController : ControllerBase
{
    // The KPI sheet is a few dozen kilobytes; anything far larger is not one
    private const long MaxWorkbookBytes = 10 * 1024 * 1024;
    private readonly IKpiRegisterService _service;

    public KpiRegisterController(IKpiRegisterService service) => _service = service;

    private int CurrentUserId => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet]
    [ProducesResponseType(typeof(KpiRegisterDto), StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] int? year) => Run(async () => Ok(await _service.ListAsync(CurrentUserId, year)));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(KpiDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(int id) => Run(async () => Ok(await _service.GetAsync(CurrentUserId, id)));

    [HttpPost]
    [ProducesResponseType(typeof(KpiDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Create([FromBody] SaveKpiDto dto) => Run(async () => Ok(await _service.CreateAsync(CurrentUserId, dto)));

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Update(int id, [FromBody] SaveKpiDto dto) =>
        Run(async () => { await _service.UpdateAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpPost("capabilities")]
    [ProducesResponseType(typeof(KpiCapabilityDto), StatusCodes.Status200OK)]
    public Task<IActionResult> CreateCapability([FromBody] SaveKpiCapabilityDto dto) =>
        Run(async () => Ok(await _service.SaveCapabilityAsync(CurrentUserId, null, dto)));

    [HttpPut("capabilities/{id:int}")]
    [ProducesResponseType(typeof(KpiCapabilityDto), StatusCodes.Status200OK)]
    public Task<IActionResult> UpdateCapability(int id, [FromBody] SaveKpiCapabilityDto dto) =>
        Run(async () => Ok(await _service.SaveCapabilityAsync(CurrentUserId, id, dto)));

    /// <summary>The owner's value for one period (period 1 for a yearly KPI, 1-4 for quarters, 1-12 for months); a null value clears it.</summary>
    [HttpPut("{id:int}/values/{year:int}/{period:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> SaveValue(int id, int year, int period, [FromBody] SaveKpiValueDto dto) =>
        Run(async () => { await _service.SaveValueAsync(CurrentUserId, id, year, period, dto); return NoContent(); });

    [HttpPut("{id:int}/targets/{year:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> SaveTarget(int id, int year, [FromBody] SaveKpiTargetDto dto) =>
        Run(async () => { await _service.SaveTargetAsync(CurrentUserId, id, year, dto); return NoContent(); });

    /// <summary>The year-end table, one column per year; this year and the three before it by default.</summary>
    [HttpGet("report")]
    [ProducesResponseType(typeof(KpiYearReportDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Report([FromQuery] int? from, [FromQuery] int? to) =>
        Run(async () =>
        {
            var toYear = to ?? DateTime.UtcNow.Year;
            return Ok(await _service.ReportAsync(CurrentUserId, from ?? toYear - 3, toYear));
        });

    /// <summary>Reads the KPI sheet and shows what would be imported; nothing is saved.</summary>
    [HttpPost("import/preview")]
    [RequestSizeLimit(MaxWorkbookBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxWorkbookBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(KpiImportPreviewDto), StatusCodes.Status200OK)]
    public Task<IActionResult> PreviewImport(IFormFile? file) =>
        Run(async () =>
        {
            await using var workbook = OpenWorkbook(file);
            return Ok(await _service.PreviewImportAsync(CurrentUserId, workbook));
        });

    [HttpPost("import")]
    [RequestSizeLimit(MaxWorkbookBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxWorkbookBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(KpiImportResultDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Import(IFormFile? file) =>
        Run(async () =>
        {
            await using var workbook = OpenWorkbook(file);
            return Ok(await _service.ImportAsync(CurrentUserId, workbook));
        });

    private static Stream OpenWorkbook(IFormFile? file)
    {
        if (file == null || file.Length == 0) throw new ArgumentException("Choose the Excel file (.xlsx).");
        if (file.Length > MaxWorkbookBytes) throw new ArgumentException("The file is larger than 10 MB.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an Excel file saved as .xlsx.");
        return file.OpenReadStream();
    }
}
