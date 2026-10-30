using System.Security.Claims;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ItsTool.API.Controllers;

/// <summary>
/// Security reports and incidents. Any signed-in user may report and see their own reports; everything else is
/// checked per incident by SecurityIncidentService against the database, not the token, so a revoked grant
/// stops working at once. An incident the caller may not see is "not found", never "forbidden".
/// </summary>
[ApiController]
[Route("api/security")]
[Authorize]
public class SecurityController : ControllerBase
{
    private readonly ISecurityIncidentService _service;

    public SecurityController(ISecurityIncidentService service) => _service = service;

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

    [HttpPost("reports")]
    [ProducesResponseType(typeof(MySecurityReportDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Report([FromBody] ReportSecurityIssueDto dto) =>
        Run(async () => Ok(await _service.ReportAsync(CurrentUserId, dto)));

    [HttpGet("reports/mine")]
    [ProducesResponseType(typeof(IReadOnlyList<MySecurityReportDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> MyReports() =>
        Run(async () => Ok(await _service.GetMyReportsAsync(CurrentUserId)));

    [HttpGet("incidents")]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityIncidentListItemDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] SecurityIncidentFilterDto filter) =>
        Run(async () => Ok(await _service.ListAsync(CurrentUserId, filter)));

    [HttpPost("incidents")]
    [ProducesResponseType(typeof(SecurityIncidentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Create([FromBody] CreateSecurityIncidentDto dto) =>
        Run(async () => Ok(await _service.CreateAsync(CurrentUserId, dto)));

    [HttpGet("incidents/{id:int}")]
    [ProducesResponseType(typeof(SecurityIncidentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(int id) =>
        Run(async () => Ok(await _service.GetAsync(CurrentUserId, id)));

    [HttpPut("incidents/{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] UpdateSecurityIncidentDto dto) =>
        Run(async () => { await _service.UpdateAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpPost("incidents/{id:int}/triage")]
    public Task<IActionResult> Triage(int id, [FromBody] TriageSecurityIncidentDto dto) =>
        Run(async () => { await _service.TriageAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpPost("incidents/{id:int}/status")]
    public Task<IActionResult> ChangeStatus(int id, [FromBody] ChangeSecurityStatusDto dto) =>
        Run(async () => { await _service.ChangeStatusAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpPost("incidents/{id:int}/notes")]
    public Task<IActionResult> AddNote(int id, [FromBody] AddSecurityNoteDto dto) =>
        Run(async () => { await _service.AddNoteAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpPost("incidents/{id:int}/members")]
    public Task<IActionResult> AddMember(int id, [FromBody] AddSecurityMemberDto dto) =>
        Run(async () => { await _service.AddMemberAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpDelete("incidents/{id:int}/members/{memberUserId:int}")]
    public Task<IActionResult> RemoveMember(int id, int memberUserId) =>
        Run(async () => { await _service.RemoveMemberAsync(CurrentUserId, id, memberUserId); return NoContent(); });

    [HttpPost("incidents/{id:int}/affected-items")]
    public Task<IActionResult> AddAffectedItems(int id, [FromBody] AddSecurityAffectedItemsDto dto) =>
        Run(async () => { await _service.AddAffectedItemsAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpPut("incidents/{id:int}/affected-items/{itemId:int}")]
    public Task<IActionResult> UpdateAffectedItem(int id, int itemId, [FromBody] UpdateSecurityAffectedItemDto dto) =>
        Run(async () => { await _service.UpdateAffectedItemAsync(CurrentUserId, id, itemId, dto); return NoContent(); });

    [HttpPut("incidents/{id:int}/review")]
    public Task<IActionResult> SaveReview(int id, [FromBody] SaveSecurityReviewDto dto) =>
        Run(async () => { await _service.SaveReviewAsync(CurrentUserId, id, dto); return NoContent(); });

    [HttpGet("incidents/{id:int}/access-log")]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityIncidentAccessDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> AccessLog(int id) =>
        Run(async () => Ok(await _service.GetAccessLogAsync(CurrentUserId, id)));
}
