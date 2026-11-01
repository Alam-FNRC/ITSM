using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// Where ticket attachments and security evidence are kept. On a server both belong on a full path outside the
/// site, such as \\fileserver\itsm\evidence: a relative path lands inside the application folder, where the next
/// deployment can overwrite it, and a mapped drive letter is not visible to the IIS application pool.
/// </summary>
public static class FileStoragePaths
{
    public const string UploadsSetting = "FileStorage:BasePath";

    /// <summary>The storage settings that are missing or not a full path, with the folder they resolve to.</summary>
    public static IReadOnlyList<(string Setting, string Folder)> NotFullyQualified(IConfiguration configuration) =>
        new[] { (UploadsSetting, "uploads"), (LocalEvidenceStore.PathSetting, "evidence") }
            .Select(s => (s.Item1, Value: configuration[s.Item1], Default: s.Item2))
            .Where(s => string.IsNullOrWhiteSpace(s.Value) || !Path.IsPathFullyQualified(s.Value))
            .Select(s => (s.Item1, Path.GetFullPath(string.IsNullOrWhiteSpace(s.Value) ? s.Default : s.Value)))
            .ToList();
}
