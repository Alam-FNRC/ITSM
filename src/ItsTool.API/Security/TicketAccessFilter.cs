using System.Security.Claims;
using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ItsTool.API.Security;

/// <summary>
/// Returns 404 for any action with an {id} route value when the caller may not see that ticket, so the check
/// also covers ticket endpoints added later. 404 rather than 403 so ticket IDs cannot be probed.
/// </summary>
public sealed class TicketAccessFilter : IAsyncActionFilter
{
    private readonly ITicketService _tickets;

    public TicketAccessFilter(ITicketService tickets) => _tickets = tickets;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.RouteData.Values.TryGetValue("id", out var routeId))
        {
            var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(routeId?.ToString(), out var ticketId) ||
                !int.TryParse(userIdClaim, out var userId) ||
                !await _tickets.CanAccessTicketAsync(ticketId, userId))
            {
                context.Result = new NotFoundResult();
                return;
            }
        }

        await next();
    }
}
