using System.Security.Cryptography;
using ItsTool.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

/// <summary>Evidence files on local disk under FileStorage:EvidencePath, one folder per incident.</summary>
public class LocalEvidenceStore : IEvidenceStore
{
    public const string PathSetting = "FileStorage:EvidencePath";
    private readonly string _basePath;

    public LocalEvidenceStore(IConfiguration configuration)
    {
        _basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuration[PathSetting] ?? "evidence"));
    }

    public async Task<StoredEvidenceFile> SaveAsync(int incidentId, Stream content, string extension, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(_basePath, incidentId.ToString());
        Directory.CreateDirectory(folder);
        // The client's file name never reaches the file system
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long size = 0;
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                size += read;
            }
        }
        catch
        {
            // A half-written file was never evidence. The share may rightly refuse deletes; the write error matters more.
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return new StoredEvidenceFile(path, size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    public Stream? OpenRead(string path)
    {
        // Only files under the evidence folder are served, whatever the database says
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
