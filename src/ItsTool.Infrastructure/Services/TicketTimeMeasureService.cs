using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ItsTool.Infrastructure.Services;

public class TicketTimeMeasureService : ITicketTimeMeasureService
{
    private const int TicketsPerBatch = 100;
    private readonly ItsToolDbContext _context;
    private readonly IConfiguration? _config;
    private readonly ILogger<TicketTimeMeasureService>? _logger;
    private BusinessCalendar? _calendar;

    public TicketTimeMeasureService(ItsToolDbContext context, IConfiguration? config = null, ILogger<TicketTimeMeasureService>? logger = null)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    private async Task<BusinessCalendar> GetCalendarAsync() =>
        _calendar ??= await BusinessCalendar.LoadAsync(_context, _config?[BusinessCalendar.TimeZoneSetting]);

    public async Task RecalculateAsync(int ticketId)
    {
        TicketTimeMeasure? measure = null;
        try
        {
            var ticket = await _context.Tickets
                .Where(t => t.Id == ticketId)
                .Select(t => new { t.CreatedAt, t.StatusId })
                .FirstOrDefaultAsync();
            if (ticket == null) return;

            var history = await _context.TicketHistories.AsNoTracking().Where(h => h.TicketId == ticketId).ToListAsync();
            var statuses = await _context.Statuses.AsNoTracking()
                .ToDictionaryAsync(s => s.Id, s => new TicketTimeCalculator.StatusFlags(s.IsClosedStatus, s.PausesSla));
            var assignments = await _context.TicketAssignments.AsNoTracking()
                .Where(a => a.TicketId == ticketId && !a.IsDeleted)
                .Select(a => new TicketTimeCalculator.Assignment(a.AssignedUserId, a.AssignedGroupId, a.CreatedAt))
                .ToListAsync();
            var groupIds = assignments.Where(a => a.GroupId.HasValue).Select(a => a.GroupId!.Value).Distinct().ToList();
            var members = (await _context.GroupMembers.AsNoTracking()
                    .Where(m => groupIds.Contains(m.GroupId) && !m.IsDeleted)
                    .Select(m => new { m.GroupId, m.UserId })
                    .ToListAsync())
                .GroupBy(m => m.GroupId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.UserId).ToHashSet());

            var result = TicketTimeCalculator.Calculate(
                new Ticket { Id = ticketId, CreatedAt = ticket.CreatedAt, StatusId = ticket.StatusId },
                history, statuses, assignments, members, await GetCalendarAsync());

            measure = await _context.TicketTimeMeasures.FirstOrDefaultAsync(m => m.TicketId == ticketId);
            if (measure == null)
            {
                measure = new TicketTimeMeasure { TicketId = ticketId };
                _context.TicketTimeMeasures.Add(measure);
            }
            Copy(result, measure);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // The change that triggered this is already saved, so it must not fail because of the measures.
            // The next change to the ticket, or FillMissingAsync, calculates them again.
            _logger?.LogError(ex, "Could not calculate the time measures of ticket {TicketId}", ticketId);
            if (measure != null) _context.Entry(measure).State = EntityState.Detached;
        }
    }

    public async Task<int> FillMissingAsync(CancellationToken cancellationToken = default)
    {
        var ticketIds = await _context.Tickets
            .Where(t => t.TimeMeasure == null)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        for (var i = 0; i < ticketIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RecalculateAsync(ticketIds[i]);
            // Keeps a large backlog from piling up in the change tracker
            if ((i + 1) % TicketsPerBatch == 0) _context.ChangeTracker.Clear();
        }
        return ticketIds.Count;
    }

    public async Task<TicketTimesDto?> GetAsync(int ticketId)
    {
        var measure = await _context.TicketTimeMeasures.AsNoTracking().FirstOrDefaultAsync(m => m.TicketId == ticketId);
        if (measure == null)
        {
            await RecalculateAsync(ticketId);
            measure = await _context.TicketTimeMeasures.AsNoTracking().FirstOrDefaultAsync(m => m.TicketId == ticketId);
            if (measure == null) return null;
        }

        var waiting = measure.WaitingMinutes;
        if (measure.WaitingSince.HasValue)
            waiting += (await GetCalendarAsync()).WorkingMinutesBetween(measure.WaitingSince.Value, DateTime.UtcNow);

        return new TicketTimesDto(
            measure.OwnedAt, measure.OwnedByUserId, measure.TimeToOwnMinutes,
            waiting, measure.WaitingSince.HasValue,
            measure.ResolvedAt, measure.TimeToResolveMinutes,
            measure.ReopenCount, measure.AssignmentCount);
    }

    private static void Copy(TicketTimeMeasure from, TicketTimeMeasure to)
    {
        to.OwnedAt = from.OwnedAt;
        to.OwnedByUserId = from.OwnedByUserId;
        to.TimeToOwnMinutes = from.TimeToOwnMinutes;
        to.WaitingMinutes = from.WaitingMinutes;
        to.WaitingSince = from.WaitingSince;
        to.ResolvedAt = from.ResolvedAt;
        to.TimeToResolveMinutes = from.TimeToResolveMinutes;
        to.ReopenCount = from.ReopenCount;
        to.AssignmentCount = from.AssignmentCount;
        to.CalculatedAt = DateTime.UtcNow;
    }
}
