using ItsTool.Application.DTOs;

namespace ItsTool.Application.Interfaces;

/// <summary>Maintains the services of the catalogue; categories are linked to them through the category screens.</summary>
public interface IServiceCatalogService
{
    Task<List<ItServiceDto>> GetAllAsync(bool activeOnly = false);
    Task<ItServiceDto?> GetAsync(int id);

    /// <exception cref="ArgumentException">The name is taken or the owner team does not exist.</exception>
    Task<ItServiceDto> CreateAsync(SaveItServiceDto dto);

    /// <exception cref="KeyNotFoundException">No such service.</exception>
    /// <exception cref="ArgumentException">The name is taken or the owner team does not exist.</exception>
    Task UpdateAsync(int id, SaveItServiceDto dto);

    /// <exception cref="KeyNotFoundException">No such service.</exception>
    /// <exception cref="InvalidOperationException">Categories still belong to the service.</exception>
    Task DeleteAsync(int id);
}
