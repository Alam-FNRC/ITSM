using ItsTool.Application.DTOs;

namespace ItsTool.Application.Interfaces;

/// <summary>Service desk KPIs for report.view holders. Covers every ticket; security incidents are kept apart and never included.</summary>
public interface IKpiService
{
    /// <exception cref="ArgumentException">The start date is after the end date, or the period is longer than two years.</exception>
    Task<KpiReportDto> GetReportAsync(KpiFilterDto filter);

    Task<KpiFilterOptionsDto> GetFilterOptionsAsync();
}
