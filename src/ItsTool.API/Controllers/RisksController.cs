using System.Security.Claims;
using System.Text.Json;
using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ItsTool.API.Controllers;

/// <summary>
/// The risk register. Rights are checked per call by RiskService against the database, not the token, so a
/// revoked grant stops working at once. A risk the caller may not see is "not found", never "forbidden".
/// </summary>
[ApiController]
[Route("api/risks")]
[Authorize]
public class RisksController : ControllerBase
{
    // The Excel register is a few hundred kilobytes; anything far larger is not one
    private const long MaxWorkbookBytes = 10 * 1024 * 1024;
    private readonly IRiskService _service;

    public RisksController(IRiskService service) => _service = service;

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
    [ProducesResponseType(typeof(RiskRegisterDto), StatusCodes.Status200OK)]
    public Task<IActionResult> List() => Run(async () => Ok(await _service.ListAsync(CurrentUserId)));

    /// <summary>The ISO 27001:2022 Annex A controls to pick from; the list is public knowledge.</summary>
    [HttpGet("annex-a")]
    [ProducesResponseType(typeof(IReadOnlyList<AnnexAControlDto>), StatusCodes.Status200OK)]
    public IActionResult AnnexA() => Ok(AnnexAControls.All.Select(c => new AnnexAControlDto(c.Code, c.Title)));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RiskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(int id) => Run(async () => Ok(await _service.GetAsync(CurrentUserId, id)));

    [HttpPost]
    [ProducesResponseType(typeof(RiskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Create([FromBody] SaveRiskDto dto) => Run(async () => Ok(await _service.CreateAsync(CurrentUserId, dto)));

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Update(int id, [FromBody] SaveRiskDto dto) =>
        Run(async () => { await _service.UpdateAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpPost("approve")]
    [ProducesResponseType(typeof(RiskBulkResultDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Approve([FromBody] RiskIdsDto dto) =>
        Run(async () => Ok(new RiskBulkResultDto(await _service.ApproveAsync(CurrentUserId, dto.Ids))));

    [HttpPost("review")]
    [ProducesResponseType(typeof(RiskBulkResultDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Review([FromBody] RiskIdsDto dto) =>
        Run(async () => Ok(new RiskBulkResultDto(await _service.MarkReviewedAsync(CurrentUserId, dto.Ids))));

    /// <summary>Reads an Excel register and shows what would be imported; nothing is saved.</summary>
    [HttpPost("import/preview")]
    [RequestSizeLimit(MaxWorkbookBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxWorkbookBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(RiskImportPreviewDto), StatusCodes.Status200OK)]
    public Task<IActionResult> PreviewImport(IFormFile? file) =>
        Run(async () =>
        {
            await using var workbook = OpenWorkbook(file);
            return Ok(await _service.PreviewImportAsync(CurrentUserId, workbook));
        });

    /// <summary>Imports the rows of an Excel register; owners maps each owner name in the file to a user id, as JSON.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaxWorkbookBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxWorkbookBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(RiskImportResultDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Import(IFormFile? file, [FromForm] string? owners) =>
        Run(async () =>
        {
            Dictionary<string, int> map;
            try
            {
                map = string.IsNullOrWhiteSpace(owners) ? new() : JsonSerializer.Deserialize<Dictionary<string, int>>(owners) ?? new();
            }
            catch (JsonException)
            {
                throw new ArgumentException("The owner list is not valid.");
            }
            await using var workbook = OpenWorkbook(file);
            return Ok(await _service.ImportAsync(CurrentUserId, workbook, map));
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
