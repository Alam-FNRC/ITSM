using System.ComponentModel.DataAnnotations;

namespace ItsTool.Application.DTOs;

// Enum values travel as their names (e.g. "Containment", "Phishing") and are checked by the service

public record ReportSecurityIssueDto(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(4000)] string Description,
    DateTime? OccurredAt);

/// <summary>What a reporter sees of their own report: that it was received and how far it is (Received, InProgress, Closed).</summary>
public record MySecurityReportDto(string Reference, string Title, DateTime ReportedAt, string Progress);

public record SecurityIncidentListItemDto(
    int Id,
    string Reference,
    string Title,
    string Source,
    string Status,
    string TriageOutcome,
    string Category,
    int? Severity,
    bool PersonalDataInvolved,
    int? HandlerUserId,
    string? HandlerName,
    DateTime ReportedAt,
    int AffectedItems,
    int OpenAffectedItems);

public record SecurityIncidentActionDto(int Id, int UserId, string Kind, string? Field, string? OldValue, string? NewValue, string? Note, DateTime CreatedAt);

public record SecurityAffectedItemDto(int Id, string Kind, string Name, string Status, string? StatusReason, DateTime? StatusChangedAt, int? StatusChangedByUserId);

public record SecurityIncidentMemberDto(int UserId, int AddedByUserId, DateTime AddedAt);

public record SecurityIncidentDetailDto(
    int Id,
    string Reference,
    string Title,
    string Description,
    string Source,
    string Status,
    int ReporterUserId,
    DateTime ReportedAt,
    DateTime? OccurredAt,
    int? HandlerUserId,
    string TriageOutcome,
    string? TriageReason,
    DateTime? TriagedAt,
    int? TriagedByUserId,
    string Category,
    int? Severity,
    bool PersonalDataInvolved,
    DateTime? ContainedAt,
    DateTime? EradicatedAt,
    DateTime? RecoveredAt,
    DateTime? ClosedAt,
    string? ReviewRootCause,
    string? ReviewWhatWorked,
    string? ReviewImprovements,
    string? ReviewFollowUps,
    DateTime? ReviewCompletedAt,
    bool ReviewRequired,
    IReadOnlyList<SecurityIncidentActionDto> Actions,
    IReadOnlyList<SecurityAffectedItemDto> AffectedItems,
    IReadOnlyList<SecurityIncidentMemberDto> Members,
    // Display names of every user the incident mentions, so members need no access to the user list
    IReadOnlyDictionary<int, string> UserNames,
    bool CanManage,
    bool CanSeeAccessLog);

public record SecurityIncidentFilterDto(string? Status = null, string? Search = null, bool OpenOnly = false);

public record CreateSecurityIncidentDto(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(4000)] string Description,
    DateTime? OccurredAt,
    string? Category,
    [Range(1, 4)] int? Severity,
    bool PersonalDataInvolved);

public record UpdateSecurityIncidentDto(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(4000)] string Description,
    DateTime? OccurredAt,
    [Required] string Category,
    [Range(1, 4)] int? Severity,
    bool PersonalDataInvolved,
    int? HandlerUserId);

public record TriageSecurityIncidentDto(
    [Required] string Outcome,
    [Required, MaxLength(2000)] string Reason,
    string? Category,
    [Range(1, 4)] int? Severity);

public record ChangeSecurityStatusDto([Required] string Status, [MaxLength(4000)] string? Note);

public record AddSecurityNoteDto([Required, MaxLength(4000)] string Text);

public record AddSecurityMemberDto([Range(1, int.MaxValue)] int UserId);

public record NewSecurityAffectedItemDto([Required] string Kind, [Required, MaxLength(300)] string Name);

public record AddSecurityAffectedItemsDto([Required, MinLength(1), MaxLength(500)] List<NewSecurityAffectedItemDto> Items);

public record UpdateSecurityAffectedItemDto([Required] string Status, [MaxLength(2000)] string? Reason);

public record SaveSecurityReviewDto(
    [MaxLength(4000)] string? RootCause,
    [MaxLength(4000)] string? WhatWorked,
    [MaxLength(4000)] string? Improvements,
    [MaxLength(4000)] string? FollowUps);

public record SecurityIncidentAccessDto(int UserId, string? UserName, DateTime OpenedAt);
