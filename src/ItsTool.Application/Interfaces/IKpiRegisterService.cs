using ItsTool.Application.DTOs;

namespace ItsTool.Application.Interfaces;

/// <summary>
/// The IT KPI register. Rights are read from the database on every call: kpi.manage keeps the definitions and sees
/// everything; a KPI's owner sees it and enters its values. A KPI the caller may not see is "not found".
/// </summary>
public interface IKpiRegisterService
{
    Task<KpiRegisterDto> ListAsync(int userId, int? year);
    Task<KpiDetailDto> GetAsync(int userId, int kpiId);
    Task<KpiDetailDto> CreateAsync(int userId, SaveKpiDto dto);
    Task UpdateAsync(int userId, int kpiId, SaveKpiDto dto);
    Task<KpiCapabilityDto> SaveCapabilityAsync(int userId, int? capabilityId, SaveKpiCapabilityDto dto);
    Task SaveValueAsync(int userId, int kpiId, int year, int period, SaveKpiValueDto dto);
    Task SaveTargetAsync(int userId, int kpiId, int year, SaveKpiTargetDto dto);
    Task<KpiYearReportDto> ReportAsync(int userId, int fromYear, int toYear);
    Task<KpiImportPreviewDto> PreviewImportAsync(int userId, Stream workbook);
    Task<KpiImportResultDto> ImportAsync(int userId, Stream workbook);
    /// <summary>Whether the user owns a KPI, so the menu shows the register to them.</summary>
    Task<bool> OwnsAnyAsync(int userId);
    /// <summary>Tells each owner once about a period that ended with no value. Returns how many notifications were sent.</summary>
    Task<int> SendRemindersAsync(DateTime utcNow);
}
