using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ItsTool.Application.Interfaces;
using ItsTool.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.API.Security;

/// <summary>
/// Checks every request's token against the database (review A10). A token used to carry its roles and permissions
/// for its whole lifetime (two hours), so removing someone's access, deactivating them or changing their password
/// had no effect until it expired. Now:
/// <list type="bullet">
/// <item>tokens of deactivated or deleted users, and tokens issued before the user's sessions were revoked
/// (password change or reset), are refused;</item>
/// <item>roles, permissions and the must-change-password flag are replaced with their current values, so policies
/// and permission checks always see the user's access as it is now.</item>
/// </list>
/// </summary>
public static class SessionValidation
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity is not ClaimsIdentity identity ||
            !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail("The token does not name a user.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<ItsToolDbContext>();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.IsDeleted, u.MustChangePassword, u.SessionsRevokedAt })
            .FirstOrDefaultAsync();

        if (user == null || !user.IsActive || user.IsDeleted)
        {
            context.Fail("The account is no longer active.");
            return;
        }

        if (user.SessionsRevokedAt is { } revokedAt && IssuedAt(principal) < TruncateToSeconds(revokedAt))
        {
            context.Fail("The session was ended by a password change.");
            return;
        }

        var roles = await db.UserRoles
            .Where(ur => ur.UserId == userId && !ur.IsDeleted && ur.Role != null && ur.Role.IsActive && !ur.Role.IsDeleted)
            .Select(ur => ur.Role!.Name)
            .ToListAsync();
        var permissions = await services.GetRequiredService<IPermissionCalculator>().CalculateEffectivePermissionsAsync(userId);

        var current = new ClaimsIdentity(
            identity.Claims.Where(c => c.Type != ClaimTypes.Role && c.Type != "permission" && c.Type != "mustChangePassword"),
            identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType);
        current.AddClaims(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        current.AddClaims(permissions.Select(permission => new Claim("permission", permission)));
        current.AddClaim(new Claim("mustChangePassword", user.MustChangePassword ? "true" : "false"));
        context.Principal = new ClaimsPrincipal(current);
    }

    // Tokens without an issue time predate this check and are treated as issued at the epoch
    private static DateTime IssuedAt(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Iat), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : DateTime.UnixEpoch;

    // The issue time has whole seconds; a token issued in the same second as the revocation is kept, so the
    // token returned by the password change itself stays valid
    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
