using ItsTool.Application.DTOs;

namespace ItsTool.Application.Interfaces;

/// <summary>
/// Security incidents. Every call takes the caller's user id and checks access itself:
/// KeyNotFoundException when the caller may not see the incident (or it does not exist),
/// UnauthorizedAccessException when they may see it but not do this, ArgumentException for invalid input.
/// </summary>
public interface ISecurityIncidentService
{
    Task<MySecurityReportDto> ReportAsync(int userId, ReportSecurityIssueDto dto);
    Task<IReadOnlyList<MySecurityReportDto>> GetMyReportsAsync(int userId);

    Task<IReadOnlyList<SecurityIncidentListItemDto>> ListAsync(int userId, SecurityIncidentFilterDto filter);
    Task<SecurityIncidentDetailDto> CreateAsync(int userId, CreateSecurityIncidentDto dto);
    Task<SecurityIncidentDetailDto> GetAsync(int userId, int incidentId);
    Task UpdateAsync(int userId, int incidentId, UpdateSecurityIncidentDto dto);
    Task TriageAsync(int userId, int incidentId, TriageSecurityIncidentDto dto);
    Task ChangeStatusAsync(int userId, int incidentId, ChangeSecurityStatusDto dto);
    Task AddNoteAsync(int userId, int incidentId, AddSecurityNoteDto dto);
    Task AddMemberAsync(int userId, int incidentId, AddSecurityMemberDto dto);
    Task RemoveMemberAsync(int userId, int incidentId, int memberUserId);
    Task AddAffectedItemsAsync(int userId, int incidentId, AddSecurityAffectedItemsDto dto);
    Task UpdateAffectedItemAsync(int userId, int incidentId, int itemId, UpdateSecurityAffectedItemDto dto);
    Task SaveReviewAsync(int userId, int incidentId, SaveSecurityReviewDto dto);
    Task<IReadOnlyList<SecurityIncidentAccessDto>> GetAccessLogAsync(int userId, int incidentId);
}
