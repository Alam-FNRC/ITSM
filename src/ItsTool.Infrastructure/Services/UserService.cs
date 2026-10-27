using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Infrastructure.Data;
using ItsTool.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly IRepository<User> _repository;
    private readonly ItsToolDbContext _context;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

    public UserService(IRepository<User> repository, ItsToolDbContext context, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _repository = repository;
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IEnumerable<UserDto>> GetAllAsync()
    {
        var users = await _context.Users.Where(u => !u.IsDeleted).Include(u => u.UserRoles).Include(u => u.PermissionOverrides).Include(u => u.GroupMemberships).ToListAsync();
        return users.Select(u => new UserDto(
            u.Id, u.Username, u.Email, u.FirstName, u.LastName, u.IsActive, u.DepartmentId,
            u.UserRoles.Select(ur => ur.RoleId).ToArray(),
            u.PermissionOverrides.ToDictionary(po => po.PermissionId, po => po.IsGranted),
            u.ProfilePhoto,
            u.GroupMemberships.Select(gm => gm.GroupId).ToArray(),
            u.CreatedAt
        ));
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var user = await _context.Users.Where(u => !u.IsDeleted).Include(u => u.UserRoles).Include(u => u.PermissionOverrides).Include(u => u.GroupMemberships).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;
        return new UserDto(
            user.Id, user.Username, user.Email, user.FirstName, user.LastName, user.IsActive, user.DepartmentId,
            user.UserRoles.Select(ur => ur.RoleId).ToArray(),
            user.PermissionOverrides.ToDictionary(po => po.PermissionId, po => po.IsGranted),
            user.ProfilePhoto,
            user.GroupMemberships.Select(gm => gm.GroupId).ToArray(),
            user.CreatedAt
        );
    }

    public async Task<UserDto> CreateAsync(CreateUserDto dto)
    {
        PasswordPolicy.Validate(dto.Password, dto.Username);
        if (await _context.Users.AnyAsync(u => !u.IsDeleted && (u.Username == dto.Username || u.Email == dto.Email)))
            throw new InvalidOperationException("A user with this username or e-mail already exists.");

        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            // The administrator knows this password, so the user replaces it at first login
            MustChangePassword = true,
            DepartmentId = dto.DepartmentId,
            ProfilePhoto = dto.ProfilePhoto
        };
        
        if (dto.GroupIds != null && dto.GroupIds.Length > 0)
        {
            foreach (var groupId in dto.GroupIds)
            {
                user.GroupMemberships.Add(new GroupMember { GroupId = groupId });
            }
        }

        var defaultRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "EndUser");
        if (defaultRole != null)
        {
            user.UserRoles.Add(new UserRole { RoleId = defaultRole.Id });
        }
        
        await _repository.AddAsync(user);
        if (defaultRole != null)
        {
            AuditUserChange(user, "RoleAssigned", "Roles", "-", defaultRole.Name);
            await _context.SaveChangesAsync();
        }
        var roleIds = user.UserRoles.Select(ur => ur.RoleId).ToArray();
        return new UserDto(user.Id, user.Username, user.Email, user.FirstName, user.LastName, user.IsActive, user.DepartmentId, roleIds, new Dictionary<int, bool>(), user.ProfilePhoto, user.GroupMemberships.Select(g => g.GroupId).ToArray(), user.CreatedAt);
    }

    public async Task UpdateAsync(int id, UpdateUserDto dto)
    {
        var user = await _context.Users.Include(u => u.GroupMemberships).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) throw new KeyNotFoundException("User not found");

        if (await _context.Users.AnyAsync(u => u.Id != id && !u.IsDeleted && u.Email == dto.Email))
            throw new InvalidOperationException("Another user already has this e-mail address.");

        user.Email = dto.Email;
        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
                if (user.IsActive && !dto.IsActive)
        {
            var openAssignments = await _context.TicketAssignments
                .Include(a => a.Ticket)
                .ThenInclude(t => t.Status)
                .Where(a => a.AssignedUserId == id && a.IsActive && !a.IsDeleted && (a.Ticket.Status == null || !a.Ticket.Status.IsClosedStatus))
                .ToListAsync();

            foreach(var a in openAssignments)
            {
                a.IsActive = false;
                
                _context.TicketHistories.Add(new ItsTool.Domain.Entities.Ticket.TicketHistory
                {
                    TicketId = a.TicketId,
                    Action = "Unassigned",
                    FieldName = "Assignments",
                    OldValue = $"User:{id}",
                    NewValue = "-",
                    CreatedBy = "System"
                });
            }
        }
        user.IsActive = dto.IsActive;
        user.DepartmentId = dto.DepartmentId;
        
        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            SetPasswordByAdmin(user, dto.Password);
        }
        
        UpdateProfilePhoto(user, dto.ProfilePhoto);
        
        if (dto.GroupIds != null)
        {
            await UpdateGroupMemberships(user, dto.GroupIds.ToList());
        }
        
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

    public async Task AssignRoleAsync(int userId, int roleId)
    {
        var user = await FindUserAsync(userId);
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId && !r.IsDeleted)
            ?? throw new KeyNotFoundException("Role not found");

        var exists = await _context.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (!exists)
        {
            _context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
            AuditUserChange(user, "RoleAssigned", "Roles", "-", role.Name);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RevokeRoleAsync(int userId, int roleId)
    {
        var ur = await _context.UserRoles
            .Include(x => x.User)
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId);
        if (ur != null)
        {
            _context.UserRoles.Remove(ur);
            AuditUserChange(ur.User!, "RoleRevoked", "Roles", ur.Role!.Name, "-");
            await _context.SaveChangesAsync();
        }
    }

    public async Task AddPermissionOverrideAsync(int userId, int permissionId, bool isGranted)
    {
        var user = await FindUserAsync(userId);
        var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Id == permissionId && !p.IsDeleted)
            ?? throw new KeyNotFoundException("Permission not found");

        var over = await _context.UserPermissionOverrides
            .FirstOrDefaultAsync(o => o.UserId == userId && o.PermissionId == permissionId);
        if (over != null && over.IsGranted == isGranted) return;

        AuditUserChange(user, "PermissionOverrideSet", permission.Key,
            over == null ? "-" : OverrideLabel(over.IsGranted), OverrideLabel(isGranted));
        if (over != null)
        {
            over.IsGranted = isGranted;
        }
        else
        {
            _context.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = userId,
                PermissionId = permissionId,
                IsGranted = isGranted
            });
        }
        await _context.SaveChangesAsync();
    }

    public async Task RemovePermissionOverrideAsync(int userId, int permissionId)
    {
        var over = await _context.UserPermissionOverrides
            .Include(o => o.User)
            .Include(o => o.Permission)
            .FirstOrDefaultAsync(o => o.UserId == userId && o.PermissionId == permissionId);
        if (over != null)
        {
            _context.UserPermissionOverrides.Remove(over);
            AuditUserChange(over.User!, "PermissionOverrideRemoved", over.Permission!.Key, OverrideLabel(over.IsGranted), "-");
            await _context.SaveChangesAsync();
        }
    }

    private static string OverrideLabel(bool isGranted) => isGranted ? "Granted" : "Denied";

    private async Task<User> FindUserAsync(int id) =>
        await _context.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted)
            ?? throw new KeyNotFoundException("User not found");

    public async Task ResetPasswordAsync(int userId, string newPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) throw new KeyNotFoundException("User not found");
        
        SetPasswordByAdmin(user, newPassword);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Sets a password chosen by an administrator. The user must replace it at next login, the account is unlocked,
    /// and the action is audited explicitly because password hashes are kept out of the automatic audit.
    /// </summary>
    private void SetPasswordByAdmin(User user, string password)
    {
        PasswordPolicy.Validate(password, user.Username);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        // Whoever was using the old password is logged out
        user.SessionsRevokedAt = DateTime.UtcNow;

        AuditUserChange(user, "PasswordReset", "Password", "-", "Set by administrator; must change at next login");
    }

    /// <summary>
    /// Records a change to a user that the automatic audit leaves out: roles, permission overrides, passwords,
    /// photos and group memberships. CreatedBy is the administrator making the change.
    /// </summary>
    private void AuditUserChange(User user, string action, string fieldName, string oldValue, string newValue)
    {
        _context.SystemAuditLogs.Add(new ItsTool.Domain.Entities.SystemAuditLog
        {
            EntityType = "User",
            EntityId = user.Id.ToString(),
            EntityName = user.Username,
            Action = action,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedBy = _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0"
        });
    }

    private void UpdateProfilePhoto(User user, string? newPhoto)
    {
        if (user.ProfilePhoto != newPhoto)
        {
            var oldPhotoStr = string.IsNullOrEmpty(user.ProfilePhoto) ? "None" : "Photo Present";
            var newPhotoStr = string.IsNullOrEmpty(newPhoto) ? "None" : "Photo Present";
            AuditUserChange(user, "ProfilePhotoUpdated", "ProfilePhoto", oldPhotoStr, newPhotoStr);
            user.ProfilePhoto = newPhoto;
        }
    }
    
    private async Task UpdateGroupMemberships(User user, List<int> newGroupIds)
    {
        var existingGroupIds = user.GroupMemberships.Select(g => g.GroupId).ToList();
        var added = newGroupIds.Except(existingGroupIds).ToList();
        var removed = existingGroupIds.Except(newGroupIds).ToList();

        if (added.Count > 0 || removed.Count > 0)
        {
            var allGroupIds = existingGroupIds.Union(newGroupIds).Distinct().ToList();
            var groups = await _context.Groups.Where(g => allGroupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name);

            var oldGroupNames = string.Join(", ", existingGroupIds.Select(id => groups.TryGetValue(id, out var name) ? name : id.ToString()));
            var newGroupNames = string.Join(", ", newGroupIds.Select(id => groups.TryGetValue(id, out var name) ? name : id.ToString()));

            AuditUserChange(user, "Updated", "GroupMemberships",
                string.IsNullOrEmpty(oldGroupNames) ? "-" : oldGroupNames,
                string.IsNullOrEmpty(newGroupNames) ? "-" : newGroupNames);
        }

        var toRemove = user.GroupMemberships.Where(g => !newGroupIds.Contains(g.GroupId)).ToList();
        foreach (var rm in toRemove) user.GroupMemberships.Remove(rm);
        
        var toAdd = newGroupIds.Where(gid => !existingGroupIds.Contains(gid)).ToList();
        foreach (var addId in toAdd)
        {
            user.GroupMemberships.Add(new GroupMember { GroupId = addId });
        }
    }
}
