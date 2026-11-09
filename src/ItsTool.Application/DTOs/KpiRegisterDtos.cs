using System.ComponentModel.DataAnnotations;

namespace ItsTool.Application.DTOs;

// IT KPI register (feature spec, "IT KPI register"). Enum values travel as their names (e.g. "Quarterly", "Up").
// Dictionaries keyed by year reach the page with string keys.

public record KpiCapabilityDto(int Id, int Number, string NameEn, string NameAr);

public record SaveKpiCapabilityDto(
    [Range(1, 999)] int Number,
    [Required, MaxLength(200)] string NameEn,
    [Required, MaxLength(200)] string NameAr);

public record KpiListItemDto(
    int Id,
    int Number,
    int CapabilityId,
    string NameEn,
    string NameAr,
    string ResultType,
    string Direction,
    string Cycle,
    string YearEnd,
    string Source,
    string? Unit,
    int? OwnerUserId,
    string? OwnerName,
    decimal? YearValue,
    decimal? Target,
    int PeriodsEntered,
    int PeriodsDue,
    bool CanEnter);

public record KpiRegisterDto(int Year, IReadOnlyList<KpiCapabilityDto> Capabilities, IReadOnlyList<KpiListItemDto> Kpis, bool CanManage);

/// <summary>A stored value, or (Computed) one worked out from the tickets for a period with no stored value.</summary>
public record KpiValueDto(int Year, string Cycle, int Period, decimal Value, string? Note, int? EnteredByUserId, DateTime? EnteredAt, bool Computed);

public record KpiTargetDto(int Year, decimal Value);

public record KpiDetailDto(
    int Id,
    int Number,
    int CapabilityId,
    string NameEn,
    string NameAr,
    string? Description,
    string? Formula,
    string? DataSource,
    string? Scope,
    string? Unit,
    string ResultType,
    string Direction,
    string Cycle,
    string YearEnd,
    string Source,
    int? OwnerUserId,
    IReadOnlyList<KpiValueDto> Values,
    IReadOnlyList<KpiTargetDto> Targets,
    // Display names of every user mentioned, so an owner needs no access to the user list
    IReadOnlyDictionary<int, string> UserNames,
    bool CanManage,
    bool CanEnter,
    DateTime CreatedAt);

public record SaveKpiDto(
    [Range(1, 999)] int Number,
    int CapabilityId,
    [Required, MaxLength(300)] string NameEn,
    [Required, MaxLength(300)] string NameAr,
    [MaxLength(2000)] string? Description = null,
    [MaxLength(1000)] string? Formula = null,
    [MaxLength(500)] string? DataSource = null,
    [MaxLength(100)] string? Scope = null,
    [MaxLength(100)] string? Unit = null,
    string? ResultType = null,
    string? Direction = null,
    string? Cycle = null,
    string? YearEnd = null,
    string? Source = null,
    int? OwnerUserId = null);

/// <summary>A value for one period; null clears it.</summary>
public record SaveKpiValueDto(decimal? Value, [MaxLength(2000)] string? Note = null);

/// <summary>A target for one year; null clears it.</summary>
public record SaveKpiTargetDto(decimal? Value);

public record KpiReportKpiDto(
    int Id,
    int Number,
    int CapabilityId,
    string NameEn,
    string NameAr,
    string? Unit,
    string ResultType,
    string Direction,
    string Cycle,
    string? OwnerName,
    IReadOnlyDictionary<int, decimal?> Values,
    IReadOnlyDictionary<int, decimal?> Targets,
    IReadOnlyDictionary<int, int> Entered);

public record KpiYearReportDto(int FromYear, int ToYear, DateTime GeneratedAt, IReadOnlyList<KpiCapabilityDto> Capabilities, IReadOnlyList<KpiReportKpiDto> Kpis);

/// <summary>
/// One row of the sheet as it would be imported. Warnings are "kind:value" (see KpiWorkbook); Skip is null for a row
/// that will be imported, else "AlreadyInRegister" or "NoName".
/// </summary>
public record KpiImportRowDto(
    int Row,
    int? Number,
    string NameAr,
    string NameEn,
    int? CapabilityNumber,
    string? CapabilityNameAr,
    string ResultType,
    string Direction,
    string Cycle,
    IReadOnlyDictionary<int, decimal> Values,
    IReadOnlyList<string> Warnings,
    string? Skip);

public record KpiImportPreviewDto(IReadOnlyList<KpiImportRowDto> Rows);

public record KpiImportResultDto(int Imported, int Skipped);
