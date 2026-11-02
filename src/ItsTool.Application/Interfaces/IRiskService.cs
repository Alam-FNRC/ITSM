using ItsTool.Application.DTOs;

namespace ItsTool.Application.Interfaces;

/// <summary>
/// The risk register. Rights are read from the database on every call: risk.manage keeps the register,
/// risk.approve approves, and a risk owner sees their own risks. A risk the caller may not see is "not found".
/// </summary>
public interface IRiskService
{
    Task<RiskRegisterDto> ListAsync(int userId);
    Task<RiskDetailDto> GetAsync(int userId, int riskId);
    Task<RiskDetailDto> CreateAsync(int userId, SaveRiskDto dto);
    Task UpdateAsync(int userId, int riskId, SaveRiskDto dto);
    Task<int> ApproveAsync(int userId, IReadOnlyList<int> riskIds);
    Task<int> MarkReviewedAsync(int userId, IReadOnlyList<int> riskIds);
    Task<RiskImportPreviewDto> PreviewImportAsync(int userId, Stream workbook);
    Task<RiskImportResultDto> ImportAsync(int userId, Stream workbook, IReadOnlyDictionary<string, int> owners);
    /// <summary>Daily: target dates coming up or passed, and the yearly review. Returns the notifications sent.</summary>
    Task<int> SendRemindersAsync(DateTime utcNow);
}
