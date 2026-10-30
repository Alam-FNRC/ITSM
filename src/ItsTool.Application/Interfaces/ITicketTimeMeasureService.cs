using ItsTool.Application.DTOs;

namespace ItsTool.Application.Interfaces;

public interface ITicketTimeMeasureService
{
    /// <summary>Works the ticket's times out again from its history and stores them.</summary>
    Task RecalculateAsync(int ticketId);

    /// <summary>Calculates times for tickets that have none yet (older or imported tickets); returns how many.</summary>
    Task<int> FillMissingAsync(CancellationToken cancellationToken = default);

    Task<TicketTimesDto?> GetAsync(int ticketId);
}
