using System.Text;

namespace ItsTool.Infrastructure.Security;

/// <summary>
/// Rules for every password the application sets (review A6). Length matters more than character mixes. BCrypt
/// reads only the first 72 bytes, so longer passwords are refused instead of being silently cut.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 64;

    /// <summary>Returns why the password is not acceptable, or null when it is.</summary>
    public static string? Check(string? password, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "The password cannot be empty.";
        if (password.Length < MinimumLength)
            return $"The password must be at least {MinimumLength} characters.";
        if (password.Length > MaximumLength || Encoding.UTF8.GetByteCount(password) > 72)
            return $"The password must be at most {MaximumLength} characters.";
        if (username is { Length: >= 3 } && password.Contains(username, StringComparison.OrdinalIgnoreCase))
            return "The password must not contain the username.";
        return null;
    }

    /// <summary>Throws an <see cref="ArgumentException"/> with the reason when the password is not acceptable.</summary>
    public static void Validate(string? password, string? username = null)
    {
        var problem = Check(password, username);
        if (problem != null) throw new ArgumentException(problem);
    }
}
