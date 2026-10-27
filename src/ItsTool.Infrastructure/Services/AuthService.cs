using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Infrastructure.Data;
using ItsTool.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly ItsToolDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPermissionCalculator _permissionCalculator;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    // Checked when the name matches no account, so an unknown name takes as long to reject as a wrong password
    private static readonly string UnknownUserHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    public AuthService(
        ItsToolDbContext context,
        ITokenService tokenService,
        IPermissionCalculator permissionCalculator,
        IConfiguration configuration,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _context = context;
        _tokenService = tokenService;
        _permissionCalculator = permissionCalculator;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // Exact match: LIKE treated % and _ in the username as wildcards (review A9)
        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                (u.Username == request.Username || u.Email == request.Username) && !u.IsDeleted);

        if (user == null)
        {
            BCrypt.Net.BCrypt.Verify(request.Password, UnknownUserHash);
            throw await LoginFailedAsync(request.Username, null, AuthFailureReasons.UnknownUser);
        }

        if (!user.IsActive)
        {
            throw await LoginFailedAsync(request.Username, user, AuthFailureReasons.InactiveAccount);
        }

        // While locked, even the right password is refused, so guessing cannot continue (review A9)
        if (user.LockoutEndsAt > DateTime.UtcNow)
        {
            throw await LoginFailedAsync(request.Username, user, AuthFailureReasons.LockedOut);
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            CountWrongPassword(user, request.Username);
            throw await LoginFailedAsync(request.Username, user, AuthFailureReasons.WrongPassword);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        RecordAuthEvent(AuthEventTypes.LoginSucceeded, request.Username, user.Id);
        await _context.SaveChangesAsync();

        var roles = await _context.UserRoles
            .Where(ur => ur.UserId == user.Id && ur.Role != null && ur.Role.IsActive)
            .Select(ur => ur.Role!.Name)
            .ToListAsync();

        var permissions = await _permissionCalculator.CalculateEffectivePermissionsAsync(user.Id);

        var token = user.MustChangePassword
            ? _tokenService.GenerateToken(user.Id, user.Username, roles, permissions, true)
            : _tokenService.GenerateToken(user.Id, user.Username, roles, permissions);

        var expiryMinutes = double.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "120");

        return new AuthResponseDto(
            Token: token,
            ExpiresAt: DateTime.UtcNow.AddMinutes(expiryMinutes),
            Username: user.Username,
            Roles: roles,
            Permissions: permissions,
            MustChangePassword: user.MustChangePassword
        );
    }

    /// <summary>Records the failed attempt. Callers get the same message whatever the reason.</summary>
    private async Task<UnauthorizedAccessException> LoginFailedAsync(string username, User? user, string reason)
    {
        RecordAuthEvent(AuthEventTypes.LoginFailed, username, user?.Id, reason);
        await _context.SaveChangesAsync();
        return new UnauthorizedAccessException("Invalid credentials.");
    }

    /// <summary>Counts a wrong password and locks the account once there are too many in a row.</summary>
    private void CountWrongPassword(User user, string username)
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= _configuration.GetValue("Auth:MaxFailedLogins", 5))
        {
            user.LockoutEndsAt = DateTime.UtcNow.AddMinutes(_configuration.GetValue("Auth:LockoutMinutes", 15));
            user.FailedLoginCount = 0;
            RecordAuthEvent(AuthEventTypes.AccountLocked, username, user.Id);
        }
    }

    private void RecordAuthEvent(string eventType, string username, int? userId, string? failureReason = null)
    {
        var request = _httpContextAccessor?.HttpContext?.Request;
        _context.AuthEvents.Add(new AuthEvent
        {
            EventType = eventType,
            Username = Truncate(username, 256),
            UserId = userId,
            FailureReason = failureReason,
            IpAddress = request?.HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = request == null ? null : Truncate(request.Headers.UserAgent.ToString(), 512)
        });
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    public async Task<MeResponseDto> GetMeAsync(int userId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted);

        if (user == null)
            throw new UnauthorizedAccessException("User not found.");

        var roles = await _context.UserRoles
            .Where(ur => ur.UserId == user.Id && ur.Role != null && ur.Role.IsActive)
            .Select(ur => ur.Role!.Name)
            .ToListAsync();

        var groups = await _context.GroupMembers
            .Where(gm => gm.UserId == user.Id && gm.Group != null && gm.Group.IsActive)
            .Select(gm => gm.Group!.Name)
            .ToListAsync();

        var permissions = await _permissionCalculator.CalculateEffectivePermissionsAsync(user.Id);

        var overrides = await _context.UserPermissionOverrides
            .Include(o => o.Permission)
            .Where(o => o.UserId == user.Id && o.IsGranted && !o.IsDeleted && o.Permission != null && o.Permission.IsActive && !o.Permission.IsDeleted)
            .Select(o => o.Permission!.Key)
            .ToListAsync();

        var kbCount = await _context.KnowledgeArticles
            .CountAsync(a => a.AuthorUserId == user.Id && !a.IsDeleted);

        return new MeResponseDto(
            Id: user.Id,
            Username: user.Username,
            Email: user.Email,
            Groups: groups,
            Roles: roles,
            Permissions: permissions,
            Overrides: overrides,
            KbArticleCount: kbCount,
            ProfilePhoto: user.ProfilePhoto,
            MustChangePassword: user.MustChangePassword
        );
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(int userId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted);

        if (user == null)
            throw new UnauthorizedAccessException("User not found or inactive.");

        var roles = await _context.UserRoles
            .Where(ur => ur.UserId == user.Id && ur.Role != null && ur.Role.IsActive && !ur.Role.IsDeleted && !ur.IsDeleted)
            .Select(ur => ur.Role!.Name)
            .ToListAsync();

        var permissions = await _permissionCalculator.CalculateEffectivePermissionsAsync(user.Id);

        var token = user.MustChangePassword
            ? _tokenService.GenerateToken(user.Id, user.Username, roles, permissions, true)
            : _tokenService.GenerateToken(user.Id, user.Username, roles, permissions);

        var expiryMinutes = double.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "120");

        return new AuthResponseDto(
            Token: token,
            ExpiresAt: DateTime.UtcNow.AddMinutes(expiryMinutes),
            Username: user.Username,
            Roles: roles,
            Permissions: permissions,
            MustChangePassword: user.MustChangePassword
        );
    }

    public async Task<AuthResponseDto> ChangePasswordAsync(int userId, ChangePasswordRequestDto request)
    {
        if (request == null)
            throw new ArgumentException("No password change was sent.");

        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("The new passwords do not match.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted);

        if (user == null)
            throw new UnauthorizedAccessException("User not found or inactive.");

        if (user.LockoutEndsAt > DateTime.UtcNow)
            throw new ArgumentException("The account is locked after too many wrong passwords. Try again later.");

        // A stolen session must not be enough to take over the account (review A6), so the current
        // password is required, and wrong guesses count towards the lockout like failed logins
        if (string.IsNullOrEmpty(request.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            CountWrongPassword(user, user.Username);
            RecordAuthEvent(AuthEventTypes.PasswordChangeFailed, user.Username, user.Id, AuthFailureReasons.WrongPassword);
            await _context.SaveChangesAsync();
            throw new ArgumentException("The current password is incorrect.");
        }

        PasswordPolicy.Validate(request.NewPassword, user.Username);
        if (request.NewPassword == request.CurrentPassword)
            throw new ArgumentException("The new password must be different from the current one.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        user.FailedLoginCount = 0;
        user.UpdatedAt = DateTime.UtcNow;
        RecordAuthEvent(AuthEventTypes.PasswordChanged, user.Username, user.Id);

        await _context.SaveChangesAsync();

        var roles = await _context.UserRoles
            .Where(ur => ur.UserId == user.Id && ur.Role != null && ur.Role.IsActive && !ur.Role.IsDeleted && !ur.IsDeleted)
            .Select(ur => ur.Role!.Name)
            .ToListAsync();

        var permissions = await _permissionCalculator.CalculateEffectivePermissionsAsync(user.Id);

        var token = _tokenService.GenerateToken(user.Id, user.Username, roles, permissions);
        var expiryMinutes = double.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "120");

        return new AuthResponseDto(
            Token: token,
            ExpiresAt: DateTime.UtcNow.AddMinutes(expiryMinutes),
            Username: user.Username,
            Roles: roles,
            Permissions: permissions,
            MustChangePassword: false
        );
    }
}
