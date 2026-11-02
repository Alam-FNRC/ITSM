using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

/// <summary>Who to notify for restricted records, such as security incidents and risks.</summary>
internal static class PermissionHolders
{
    /// <summary>Active users holding a permission through a role, a group's role or a grant, minus denies; mirrors PermissionCalculator.</summary>
    public static async Task<List<int>> FindAsync(ItsToolDbContext context, string key)
    {
        var permissionId = await context.Permissions.Where(p => p.Key == key && p.IsActive && !p.IsDeleted).Select(p => (int?)p.Id).FirstOrDefaultAsync();
        if (permissionId == null) return new List<int>();

        var roleIds = await context.RolePermissions
            .Where(rp => rp.PermissionId == permissionId && !rp.IsDeleted && rp.Role != null && rp.Role.IsActive && !rp.Role.IsDeleted)
            .Select(rp => rp.RoleId).ToListAsync();
        var viaRoles = await context.UserRoles.Where(ur => roleIds.Contains(ur.RoleId) && !ur.IsDeleted).Select(ur => ur.UserId).ToListAsync();
        var groupIds = await context.GroupRoles.Where(gr => roleIds.Contains(gr.RoleId) && !gr.IsDeleted).Select(gr => gr.GroupId).ToListAsync();
        var viaGroups = await context.GroupMembers
            .Where(gm => groupIds.Contains(gm.GroupId) && !gm.IsDeleted && gm.Group != null && gm.Group.IsActive && !gm.Group.IsDeleted)
            .Select(gm => gm.UserId).ToListAsync();
        var overrides = await context.UserPermissionOverrides.Where(o => o.PermissionId == permissionId && !o.IsDeleted)
            .Select(o => new { o.UserId, o.IsGranted }).ToListAsync();

        var holders = viaRoles.Concat(viaGroups).Concat(overrides.Where(o => o.IsGranted).Select(o => o.UserId)).ToHashSet();
        holders.ExceptWith(overrides.Where(o => !o.IsGranted).Select(o => o.UserId));
        return await context.Users.Where(u => holders.Contains(u.Id) && u.IsActive && !u.IsDeleted).Select(u => u.Id).ToListAsync();
    }
}

internal static class EnumNames
{
    /// <summary>A member by its name only: Enum.TryParse would also take numbers, including ones no member has.</summary>
    public static T Parse<T>(string? value, string field) where T : struct, Enum
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || char.IsDigit(text[0]) || text[0] == '-' ||
            !Enum.TryParse<T>(text, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException($"{field} '{value}' is not valid.");
        }
        return parsed;
    }
}
