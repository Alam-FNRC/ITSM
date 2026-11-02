using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Catalog;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

public class ServiceCatalogService : IServiceCatalogService
{
    private readonly ItsToolDbContext _context;

    public ServiceCatalogService(ItsToolDbContext context) => _context = context;

    public async Task<List<ItServiceDto>> GetAllAsync(bool activeOnly = false) =>
        await _context.ItServices.AsNoTracking()
            .Where(s => !s.IsDeleted && (!activeOnly || s.IsActive))
            .OrderBy(s => s.Family).ThenBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new ItServiceDto(s.Id, s.Name, s.Family, s.Description, s.OwnerGroupId, s.SortOrder, s.IsActive,
                s.Categories.Count(c => !c.IsDeleted)))
            .ToListAsync();

    public async Task<ItServiceDto?> GetAsync(int id) =>
        await _context.ItServices.AsNoTracking()
            .Where(s => s.Id == id && !s.IsDeleted)
            .Select(s => new ItServiceDto(s.Id, s.Name, s.Family, s.Description, s.OwnerGroupId, s.SortOrder, s.IsActive,
                s.Categories.Count(c => !c.IsDeleted)))
            .FirstOrDefaultAsync();

    public async Task<ItServiceDto> CreateAsync(SaveItServiceDto dto)
    {
        var service = new ItService();
        await ApplyAsync(service, dto);
        _context.ItServices.Add(service);
        await _context.SaveChangesAsync();
        return (await GetAsync(service.Id))!;
    }

    public async Task UpdateAsync(int id, SaveItServiceDto dto)
    {
        var service = await _context.ItServices.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted)
            ?? throw new KeyNotFoundException("Service not found.");
        await ApplyAsync(service, dto);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var service = await _context.ItServices.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted)
            ?? throw new KeyNotFoundException("Service not found.");
        if (await _context.Categories.AnyAsync(c => c.ServiceId == id && !c.IsDeleted))
            throw new InvalidOperationException("Move this service's categories to another service first.");

        service.IsDeleted = true;
        service.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task ApplyAsync(ItService service, SaveItServiceDto dto)
    {
        var name = dto.Name.Trim();
        if (name.Length == 0) throw new ArgumentException("Enter the service's name.");
        if (await _context.ItServices.AnyAsync(s => s.Id != service.Id && !s.IsDeleted && s.Name == name))
            throw new ArgumentException("A service with this name already exists.");
        if (dto.OwnerGroupId.HasValue && !await _context.Groups.AnyAsync(g => g.Id == dto.OwnerGroupId && !g.IsDeleted))
            throw new ArgumentException("The owner team does not exist.");

        service.Name = name;
        service.Family = string.IsNullOrWhiteSpace(dto.Family) ? null : dto.Family.Trim();
        service.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        service.OwnerGroupId = dto.OwnerGroupId;
        service.SortOrder = dto.SortOrder;
        service.IsActive = dto.IsActive;
    }
}
