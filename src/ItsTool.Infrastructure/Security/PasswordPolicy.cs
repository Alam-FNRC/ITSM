using System.Text;

namespace ItsTool.Infrastructure.Security;

/// <summary>
/// Rules for every password the application sets: FNRC's ISO password standard (decided 10 Nov 2026): at least
/// 8 characters with an upper-case letter, a lower-case letter, a digit and a symbol. It may contain the username,
/// which at FNRC is the staff member's digital ID. BCrypt reads only the first 72 bytes, so longer passwords are refused instead of being silently cut.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 64;

    /// <summary>Returns why the password is not acceptable, or null when it is.</summary>
    public static string? Check(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "The password cannot be empty.";
        if (password.Length < MinimumLength)
            return $"The password must be at least {MinimumLength} characters.";
        if (password.Length > MaximumLength || Encoding.UTF8.GetByteCount(password) > 72)
            return $"The password must be at most {MaximumLength} characters.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit))
            return "The password must contain an upper-case letter, a lower-case letter, a digit and a symbol.";
        return null;
    }

    /// <summary>Throws an <see cref="ArgumentException"/> with the reason when the password is not acceptable.</summary>
    public static void Validate(string? password)
    {
        var problem = Check(password);
        if (problem != null) throw new ArgumentException(problem);
    }
}
