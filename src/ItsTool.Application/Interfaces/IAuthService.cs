using ItsTool.Application.DTOs;

namespace ItsTool.Application.Interfaces;

public interface ITokenService
{
    string GenerateToken(int userId, string username, IEnumerable<string> roles, IEnumerable<string> permissions);
    string GenerateToken(int userId, string username, IEnumerable<string> roles, IEnumerable<string> permissions, bool mustChangePassword);
    /// <summary>With extra claims, such as "kpiOwner" for a user who owns a KPI, which the menu reads.</summary>
    string GenerateToken(int userId, string username, IEnumerable<string> roles, IEnumerable<string> permissions, bool mustChangePassword, IReadOnlyDictionary<string, string> extraClaims);
}

public interface IAuthService
{
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request);
    Task<MeResponseDto> GetMeAsync(int userId);
    Task<AuthResponseDto> RefreshTokenAsync(int userId);
    Task<AuthResponseDto> ChangePasswordAsync(int userId, ChangePasswordRequestDto request);
}
