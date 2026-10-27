using Microsoft.Extensions.Configuration;

namespace ItsTool.UnitTests;

internal static class TestConfiguration
{
    /// <summary>The AI copilot is off by default; tests of its behaviour turn it on.</summary>
    public static IConfiguration AiEnabled() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Enabled"] = "true" }).Build();
}
