using System.ComponentModel.DataAnnotations;

namespace ItsTool.Application.DTOs;

// Risk register (feature spec, "Risk register"). Enum values travel as their names (e.g. "Mitigate", "Treating").
// Levels are "Low", "Medium" or "High" (RiskScore).

public record RiskListItemDto(
    int Id,
    int Number,
    string Title,
    double Score,
    string Level,
    double? ResidualScore,
    string? ResidualLevel,
    string Treatment,
    string Status,
    int? OwnerUserId,
    string? OwnerName,
    DateOnly? TargetDate,
    bool Overdue,
    bool Approved,
    DateTime? LastReviewedAt,
    bool ReviewDue);

public record RiskRegisterDto(IReadOnlyList<RiskListItemDto> Risks, bool CanManage, bool CanApprove);

public record RiskHistoryDto(int Id, int UserId, string Kind, string? Field, string? OldValue, string? NewValue, string? Note, DateTime CreatedAt);

public record RiskDetailDto(
    int Id,
    int Number,
    string Title,
    string? Threat,
    string? Vulnerability,
    string? ExistingControls,
    int ImpactConfidentiality,
    int ImpactIntegrity,
    int ImpactAvailability,
    int Likelihood,
    double Score,
    string Level,
    string Treatment,
    string? RecommendedControls,
    IReadOnlyList<string> AnnexAControls,
    int? OwnerUserId,
    DateOnly? TargetDate,
    string Status,
    int? ResidualConfidentiality,
    int? ResidualIntegrity,
    int? ResidualAvailability,
    int? ResidualLikelihood,
    double? ResidualScore,
    string? ResidualLevel,
    DateTime? ApprovedAt,
    int? ApprovedByUserId,
    DateTime? LastReviewedAt,
    int? LastReviewedByUserId,
    bool Overdue,
    bool ReviewDue,
    DateTime CreatedAt,
    IReadOnlyList<RiskHistoryDto> History,
    // Display names of every user the risk mentions, so an owner needs no access to the user list
    IReadOnlyDictionary<int, string> UserNames,
    bool CanManage,
    bool CanApprove);

public record SaveRiskDto(
    [Required, MaxLength(300)] string Title,
    [MaxLength(1000)] string? Threat,
    [MaxLength(2000)] string? Vulnerability,
    [MaxLength(2000)] string? ExistingControls,
    [Range(1, 5)] int ImpactConfidentiality,
    [Range(1, 5)] int ImpactIntegrity,
    [Range(1, 5)] int ImpactAvailability,
    [Range(1, 5)] int Likelihood,
    string? Treatment = null,
    [MaxLength(2000)] string? RecommendedControls = null,
    IReadOnlyList<string>? AnnexAControls = null,
    int? OwnerUserId = null,
    DateOnly? TargetDate = null,
    string? Status = null,
    [Range(1, 5)] int? ResidualConfidentiality = null,
    [Range(1, 5)] int? ResidualIntegrity = null,
    [Range(1, 5)] int? ResidualAvailability = null,
    [Range(1, 5)] int? ResidualLikelihood = null);

/// <summary>Risks to approve or to mark as reviewed in one go.</summary>
public record RiskIdsDto([Required, MinLength(1)] IReadOnlyList<int> Ids);

public record RiskBulkResultDto(int Changed);

public record AnnexAControlDto(string Code, string Title);

/// <summary>
/// One row of an Excel register as it would be imported. Warnings are "kind:value" (see RiskWorkbook); Skip is
/// null for a row that will be imported, else "AlreadyInRegister" or "NoRatings".
/// </summary>
public record RiskImportRowDto(
    int Row,
    int? Number,
    string Title,
    double Score,
    string Level,
    string Treatment,
    string Status,
    string? Owner,
    DateOnly? TargetDate,
    IReadOnlyList<string> AnnexAControls,
    IReadOnlyList<string> Warnings,
    string? Skip);

/// <summary>The rows found, and the owner names in them; each name is matched to a user before importing.</summary>
public record RiskImportPreviewDto(IReadOnlyList<RiskImportRowDto> Rows, IReadOnlyList<string> Owners);

public record RiskImportResultDto(int Imported, int Skipped);
