using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Infrastructure.Data;
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
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _configuration.GetValue("Auth:MaxFailedLogins", 5))
            {
                user.LockoutEndsAt = DateTime.UtcNow.AddMinutes(_configuration.GetValue("Auth:LockoutMinutes", 15));
                user.FailedLoginCount = 0;
                RecordAuthEvent(AuthEventTypes.AccountLocked, request.Username, user.Id);
            }
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
            throw new ArgumentException("İstek verisi boş olamaz.");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || string.IsNullOrWhiteSpace(request.ConfirmPassword))
            throw new ArgumentException("Yeni şifre ve şifre tekrarı boş olamaz.");

        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("Girilen şifreler birbiriyle eşleşmiyor.");

        if (request.NewPassword.Length < 6)
            throw new ArgumentException("Yeni şifre en az 6 karakter uzunluğunda olmalıdır.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted);

        if (user == null)
            throw new UnauthorizedAccessException("Kullanıcı bulunamadı veya hesabı aktif değil.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;

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
