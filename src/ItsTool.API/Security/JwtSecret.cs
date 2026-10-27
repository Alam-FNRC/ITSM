using System.Security.Cryptography;

namespace ItsTool.API.Security;

/// <summary>
/// Chooses the key that signs and checks login tokens. Anyone who knows it can mint a token for any user, so it
/// must come from dotnet user-secrets or the Jwt__Secret environment variable, never from this repo (review A7).
/// </summary>
public static class JwtSecret
{
    public const int MinimumLength = 32;

    // Published in this repository at some point, so never acceptable as a real key
    private static readonly HashSet<string> PublishedValues = new()
    {
        "PLACEHOLDER_SECRET_USE_USER_SECRETS",
        "fallback_secret_for_development_purposes_only",
        "enterprise_super_secret_jwt_key_turkcell_itsm_2026!"
    };

    /// <summary>
    /// Returns the configured secret. With none configured, Development gets a random key for this run (logins then
    /// end when the API restarts) and every other environment refuses to start.
    /// </summary>
    public static string Resolve(string? configured, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (isDevelopment) return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            throw new InvalidOperationException(
                "Jwt:Secret is not configured. Set it with dotnet user-secrets or the Jwt__Secret environment variable.");
        }

        if (PublishedValues.Contains(configured))
        {
            throw new InvalidOperationException("Jwt:Secret is a value published in the repository. Generate a new random secret.");
        }

        if (configured.Length < MinimumLength)
        {
            throw new InvalidOperationException($"Jwt:Secret must be at least {MinimumLength} characters.");
        }

        return configured;
    }
}
