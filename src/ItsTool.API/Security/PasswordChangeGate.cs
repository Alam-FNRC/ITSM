namespace ItsTool.API.Security;

/// <summary>Marks endpoints a user may call while they still have to replace a temporary password.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowedBeforePasswordChangeAttribute : Attribute
{
}

public static class PasswordChangeGate
{
    /// <summary>
    /// Until a user replaces a temporary password, the API answers 403 to everything except the endpoints marked
    /// <see cref="AllowedBeforePasswordChangeAttribute"/>. Before, only the browser enforced this (review A6).
    /// </summary>
    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.User.HasClaim("mustChangePassword", "true") &&
            context.GetEndpoint()?.Metadata.GetMetadata<AllowedBeforePasswordChangeAttribute>() == null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Change your password before continuing." });
            return;
        }

        await next();
    });
}
