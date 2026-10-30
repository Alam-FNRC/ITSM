using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.UnitTests.Services;

public class TicketTimeMeasureServiceTests : TestBase
{
    private readonly TicketTimeMeasureService _service;

    public TicketTimeMeasureServiceTests() => _service = new TicketTimeMeasureService(_context);

    private async Task<(Ticket Ticket, Status Open, Status OnHold)> CreateTicketAsync()
    {
        var open = new Status { Name = "Open", IsSystemDefault = true };
        var onHold = new Status { Name = "On hold", PausesSla = true };
        _context.Statuses.AddRange(open, onHold);
        await _context.SaveChangesAsync();
        var ticket = new Ticket { TicketNumber = "T-1", Title = "Printer", StatusId = open.Id, CreatedAt = DateTime.UtcNow.AddHours(-3) };
        _context.Tickets.Add(ticket);
        await _context.SaveChangesAsync();
        return (ticket, open, onHold);
    }

    [Fact]
    public async Task Recalculating_keeps_one_row_per_ticket()
    {
        var (ticket, open, onHold) = await CreateTicketAsync();

        await _service.RecalculateAsync(ticket.Id);
        _context.TicketHistories.Add(new TicketHistory { TicketId = ticket.Id, Action = "StatusChanged", FieldName = "StatusId", OldValue = open.Id.ToString(), NewValue = onHold.Id.ToString(), CreatedBy = "1", CreatedAt = DateTime.UtcNow.AddHours(-1) });
        await _context.SaveChangesAsync();
        await _service.RecalculateAsync(ticket.Id);

        var measure = await _context.TicketTimeMeasures.SingleAsync();
        Assert.Equal(ticket.Id, measure.TicketId);
        Assert.NotNull(measure.WaitingSince);
    }

    [Fact]
    public async Task The_wait_still_going_on_is_included_when_read()
    {
        var (ticket, open, onHold) = await CreateTicketAsync();
        _context.TicketHistories.Add(new TicketHistory { TicketId = ticket.Id, Action = "StatusChanged", FieldName = "StatusId", OldValue = open.Id.ToString(), NewValue = onHold.Id.ToString(), CreatedBy = "1", CreatedAt = DateTime.UtcNow.AddHours(-1) });
        await _context.SaveChangesAsync();

        // No business hours configured, so every minute counts
        var times = await _service.GetAsync(ticket.Id);

        Assert.NotNull(times);
        Assert.True(times!.IsWaiting);
        Assert.InRange(times.WaitingMinutes, 59, 61);
    }

    [Fact]
    public async Task Missing_times_are_filled_in()
    {
        var (ticket, _, _) = await CreateTicketAsync();

        Assert.Equal(1, await _service.FillMissingAsync());
        Assert.Equal(0, await _service.FillMissingAsync());
        Assert.True(await _context.TicketTimeMeasures.AnyAsync(m => m.TicketId == ticket.Id));
    }

    [Fact]
    public async Task An_unknown_ticket_has_no_times()
    {
        Assert.Null(await _service.GetAsync(404));
    }
}
