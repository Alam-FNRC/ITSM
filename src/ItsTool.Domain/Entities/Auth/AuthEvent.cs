namespace ItsTool.Domain.Entities.Auth;

/// <summary>
/// One authentication event, such as a login attempt or a password change. Append-only evidence for access
/// monitoring (ISO 27001 A.8.15): the application only ever inserts these rows.
/// </summary>
public class AuthEvent
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    /// <summary>The account the event belongs to, when the name matched one.</summary>
    public int? UserId { get; set; }
    /// <summary>The name that was typed, kept even when it matched no account.</summary>
    public string Username { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public static class AuthEventTypes
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
}

public static class AuthFailureReasons
{
    public const string UnknownUser = "UnknownUser";
    public const string WrongPassword = "WrongPassword";
    public const string InactiveAccount = "InactiveAccount";
}
