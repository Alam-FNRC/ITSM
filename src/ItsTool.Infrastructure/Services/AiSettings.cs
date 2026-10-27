using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// The AI copilot sends ticket text, including internal notes, to an external model. It stays off until a
/// provider is approved. AI:Enabled=true turns it on; close review findings A2 and A4 before doing so.
/// </summary>
public static class AiSettings
{
    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>("AI:Enabled");
}
