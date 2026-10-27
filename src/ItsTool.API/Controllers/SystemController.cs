using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ItsTool.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SystemController : ControllerBase
{
    public record SystemResponse(string Message);

    // Creates users and tickets from a parsed e-mail, so only administrators (or a service account) may call it
    [Authorize(Policy = "RequirePermission:admin.manage")]
    [HttpPost("ingest-email")]
    public async Task<IActionResult> IngestEmail(
        [FromServices] ItsTool.Application.Interfaces.IEmailIngestionService emailService,
        [FromBody] ItsTool.Application.DTOs.EmailIngestionDto dto)
    {
        await emailService.ProcessIncomingEmailAsync(dto);
        return Ok(new SystemResponse("Email processed"));
    }
}
