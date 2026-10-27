using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Data;

/// <summary>
/// The first administrator of a new installation, read from Bootstrap:AdminUsername, Bootstrap:AdminEmail and
/// Bootstrap:AdminPassword (dotnet user-secrets or environment variables, never appsettings).
/// </summary>
public sealed record FirstAdminSettings(string Username, string Email, string Password)
{
    public const int MinimumPasswordLength = 12;

    /// <summary>Returns null when no bootstrap administrator is configured; throws when it is configured badly.</summary>
    public static FirstAdminSettings? FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Bootstrap");
        var username = section["AdminUsername"];
        var email = section["AdminEmail"];
        var password = section["AdminPassword"];

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set all of Bootstrap:AdminUsername, Bootstrap:AdminEmail and Bootstrap:AdminPassword, or none.");
        }

        if (password.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException($"Bootstrap:AdminPassword must be at least {MinimumPasswordLength} characters.");
        }

        return new FirstAdminSettings(username.Trim(), email.Trim(), password);
    }
}
