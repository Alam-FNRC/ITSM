using System.ComponentModel.DataAnnotations;

namespace ItsTool.Application.DTOs;

/// <summary>A service in the catalogue; CategoryCount is the number of categories under it.</summary>
public record ItServiceDto(int Id, string Name, string? Family, string? Description, int? OwnerGroupId, int SortOrder, bool IsActive, int CategoryCount);

public record SaveItServiceDto(
    [Required, MaxLength(200)] string Name,
    [MaxLength(100)] string? Family,
    [MaxLength(1000)] string? Description,
    int? OwnerGroupId,
    int SortOrder = 0,
    bool IsActive = true);
