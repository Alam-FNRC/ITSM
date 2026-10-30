using ItsTool.Application.Interfaces;

namespace ItsTool.API.HostedServices;

/// <summary>
/// Calculates the time measures once at startup for tickets that have none: tickets created before the
/// measures existed, by email ingestion, or imported from the old tool. Later changes keep them up to date.
/// </summary>
public class TicketTimeBackfillService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TicketTimeBackfillService> _logger;

    public TicketTimeBackfillService(IServiceProvider services, ILogger<TicketTimeBackfillService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var measures = scope.ServiceProvider.GetRequiredService<ITicketTimeMeasureService>();
            var count = await measures.FillMissingAsync(stoppingToken);
            if (count > 0) _logger.LogInformation("Calculated time measures for {Count} tickets.", count);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the remaining tickets are done at the next start
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not fill in the ticket time measures.");
        }
    }
}
