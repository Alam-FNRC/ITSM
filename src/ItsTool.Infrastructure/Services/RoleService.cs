using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

public class RoleService : IRoleService
{
    private readonly IRepository<Role> _repository;
    private readonly ItsToolDbContext _context;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public RoleService(IRepository<Role> repository, ItsToolDbContext context, IHttpContextAccessor? httpContextAccessor = null)
    {
        _repository = repository;
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IEnumerable<RoleDto>> GetAllAsync()
    {
        var roles = await _context.Roles
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .ToListAsync();
            
        return roles.Select(r => new RoleDto(
            r.Id, 
            r.Name, 
            r.Description, 
            r.IsActive, 
            r.RolePermissions.Select(rp => rp.Permission!.Key).ToArray()
        ));
    }

    public async Task<RoleDto?> GetByIdAsync(int id)
    {
        var r = await _context.Roles
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(x => x.Id == id);
            
        if (r == null) return null;
        return new RoleDto(
            r.Id, 
            r.Name, 
            r.Description, 
            r.IsActive, 
            r.RolePermissions.Select(rp => rp.Permission!.Key).ToArray()
        );
    }

    public async Task<RoleDto> CreateAsync(CreateRoleDto dto)
    {
        var r = new Role { Name = dto.Name, Description = dto.Description };
        await _repository.AddAsync(r);
        
        if (dto.Permissions != null && dto.Permissions.Length > 0)
        {
            var perms = await _context.Permissions
                .Where(p => dto.Permissions.Contains(p.Key))
                .ToListAsync();

            foreach (var p in perms)
            {
                _context.RolePermissions.Add(new RolePermission { RoleId = r.Id, PermissionId = p.Id });
                AuditPermissionChange(r, "PermissionAdded", "-", p.Key);
            }
            await _context.SaveChangesAsync();
        }
        
        return new RoleDto(r.Id, r.Name, r.Description, r.IsActive, dto.Permissions ?? Array.Empty<string>());
    }

    public async Task UpdateAsync(int id, UpdateRoleDto dto)
    {
        var r = await _context.Roles
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(x => x.Id == id);
            
        if (r == null) throw new KeyNotFoundException("Role not found");
        
        r.Name = dto.Name;
        r.Description = dto.Description;
        r.IsActive = dto.IsActive;
        
        if (dto.Permissions != null)
        {
            var dbPerms = await _context.Permissions.ToListAsync();
            var targetKeys = dto.Permissions;
            
            // Remove permissions not in targetKeys
            var toRemove = r.RolePermissions.Where(rp => !targetKeys.Contains(rp.Permission!.Key)).ToList();
            foreach (var rm in toRemove)
            {
                _context.RolePermissions.Remove(rm);
                AuditPermissionChange(r, "PermissionRemoved", rm.Permission!.Key, "-");
            }

            // Add new permissions
            var currentKeys = r.RolePermissions.Select(rp => rp.Permission!.Key).ToList();
            var newKeys = targetKeys.Except(currentKeys).ToList();
            var newPerms = dbPerms.Where(p => newKeys.Contains(p.Key)).ToList();

            foreach (var p in newPerms)
            {
                _context.RolePermissions.Add(new RolePermission { RoleId = r.Id, PermissionId = p.Id });
                AuditPermissionChange(r, "PermissionAdded", "-", p.Key);
            }
        }
        
        await _context.SaveChangesAsync();
        await _repository.UpdateAsync(r);
    }

    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

    public async Task AssignPermissionAsync(int roleId, int permissionId)
    {
        var exists = await _context.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);
        if (!exists)
        {
            var role = await _context.Roles.FirstOrDefaultAsync(x => x.Id == roleId) ?? throw new KeyNotFoundException("Role not found");
            var permission = await _context.Permissions.FirstOrDefaultAsync(x => x.Id == permissionId) ?? throw new KeyNotFoundException("Permission not found");
            _context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
            AuditPermissionChange(role, "PermissionAdded", "-", permission.Key);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RevokePermissionAsync(int roleId, int permissionId)
    {
        var rp = await _context.RolePermissions
            .Include(x => x.Role)
            .Include(x => x.Permission)
            .FirstOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permissionId);
        if (rp != null)
        {
            _context.RolePermissions.Remove(rp);
            AuditPermissionChange(rp.Role!, "PermissionRemoved", rp.Permission!.Key, "-");
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>Role permissions are left out of the automatic audit, so each grant and removal is recorded here.</summary>
    private void AuditPermissionChange(Role role, string action, string oldValue, string newValue)
    {
        _context.SystemAuditLogs.Add(new SystemAuditLog
        {
            EntityType = "Role",
            EntityId = role.Id.ToString(),
            EntityName = role.Name,
            Action = action,
            FieldName = "Permissions",
            OldValue = oldValue,
            NewValue = newValue,
            CreatedBy = _httpContextAccessor?.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0"
        });
    }
}
