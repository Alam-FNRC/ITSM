using ItsTool.Application.Interfaces;

namespace ItsTool.API.HostedServices;

/// <summary>
/// Reminds risk owners before a treatment's target date and when it passes, and the security team once a month
/// while the yearly review is due. Each reminder is sent once, so checking every few hours is safe.
/// </summary>
public class RiskReminderService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly IServiceProvider _services;
    private readonly ILogger<RiskReminderService> _logger;

    public RiskReminderService(IServiceProvider services, ILogger<RiskReminderService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<IRiskService>().SendRemindersAsync(DateTime.UtcNow);
                if (sent > 0) _logger.LogInformation("Sent {Count} risk reminders.", sent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Risk reminders failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
