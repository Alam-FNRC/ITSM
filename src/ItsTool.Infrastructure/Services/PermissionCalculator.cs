using ItsTool.Application.Constants;
using ItsTool.Application.Interfaces;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

public class PermissionCalculator : IPermissionCalculator
{
    private readonly ItsToolDbContext _context;

    public PermissionCalculator(ItsToolDbContext context)
    {
        _context = context;
    }

    public async Task<HashSet<string>> CalculateEffectivePermissionsAsync(int userId)
    {
        var permissions = new HashSet<string>();

        // 0. SuperAdmin Check
        bool isSuperAdmin = await _context.UserRoles
            .Include(ur => ur.Role)
            .AnyAsync(ur => ur.UserId == userId && ur.Role != null && ur.Role.Name == "SuperAdmin" && ur.Role.IsActive && !ur.Role.IsDeleted && !ur.IsDeleted);


        // 1. Kullanıcının doğrudan rollerinden gelen yetkiler
        var userRolePerms = await _context.UserRoles
            .Where(ur => ur.UserId == userId && ur.Role != null && ur.Role.IsActive && !ur.Role.IsDeleted && !ur.IsDeleted)
            .Join(_context.RolePermissions, 
                  ur => ur.RoleId, 
                  rp => rp.RoleId, 
                  (ur, rp) => rp)
            .Where(rp => !rp.IsDeleted && rp.Permission != null && rp.Permission.IsActive && !rp.Permission.IsDeleted)
            .Select(rp => rp.Permission!.Key)
            .ToListAsync();
            
        foreach (var p in userRolePerms)
        {
            permissions.Add(p);
        }

        // 2. Kullanıcının dahil olduğu grupların rollerinden gelen yetkiler
        var groupRolePerms = await _context.GroupMembers
            .Where(gm => gm.UserId == userId && !gm.IsDeleted && gm.Group != null && gm.Group.IsActive && !gm.Group.IsDeleted)
            .Join(_context.GroupRoles, 
                  gm => gm.GroupId, 
                  gr => gr.GroupId, 
                  (gm, gr) => gr)
            .Where(gr => !gr.IsDeleted)
            .Join(_context.RolePermissions, 
                  gr => gr.RoleId, 
                  rp => rp.RoleId, 
                  (gr, rp) => rp)
            .Where(rp => !rp.IsDeleted && rp.Permission != null && rp.Permission.IsActive && !rp.Permission.IsDeleted)
            .Select(rp => rp.Permission!.Key)
            .ToListAsync();
            
        foreach (var p in groupRolePerms)
        {
            permissions.Add(p);
        }

        // 3. Per-user overrides win over roles: a grant adds the permission, a deny removes it even when a role
        // or group grants it. SuperAdmin returned above and is not affected.
        var overrides = await _context.UserPermissionOverrides
            .Where(o => o.UserId == userId && !o.IsDeleted && o.Permission != null && o.Permission.IsActive && !o.Permission.IsDeleted)
            .Select(o => new { o.Permission!.Key, o.IsGranted })
            .ToListAsync();

        foreach (var o in overrides)
        {
            if (o.IsGranted) permissions.Add(o.Key);
            else permissions.Remove(o.Key);
        }

        // SuperAdmin holds every other permission whatever the overrides say; the explicit-only ones it holds
        // only when granted above, like anyone else
        if (isSuperAdmin)
        {
            var explicitOnly = PermissionConstants.ExplicitOnly;
            var allPerms = await _context.Permissions
                .Where(p => p.IsActive && !p.IsDeleted && !explicitOnly.Contains(p.Key))
                .Select(p => p.Key)
                .ToListAsync();
            permissions.UnionWith(allPerms);
        }

        return permissions;
    }
}
