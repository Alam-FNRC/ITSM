using ItsTool.Application.Interfaces;

namespace ItsTool.API.HostedServices;

/// <summary>
/// Reminds a KPI's owner once when a period has ended with no value entered. Each reminder is sent once per
/// period, so checking every few hours is safe.
/// </summary>
public class KpiReminderService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly IServiceProvider _services;
    private readonly ILogger<KpiReminderService> _logger;

    public KpiReminderService(IServiceProvider services, ILogger<KpiReminderService> logger)
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
                var sent = await scope.ServiceProvider.GetRequiredService<IKpiRegisterService>().SendRemindersAsync(DateTime.UtcNow);
                if (sent > 0) _logger.LogInformation("Sent {Count} KPI reminders.", sent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KPI reminders failed.");
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
