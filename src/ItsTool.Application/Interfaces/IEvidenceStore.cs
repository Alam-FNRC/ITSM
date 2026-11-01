namespace ItsTool.Application.Interfaces;

/// <summary>A stored evidence file: where it is, its size and its lower-case hex SHA-256.</summary>
public record StoredEvidenceFile(string Path, long Size, string Sha256);

/// <summary>
/// Keeps security incident evidence files apart from ticket attachments. A file is written once under a new name
/// and never overwritten; there is no delete.
/// </summary>
public interface IEvidenceStore
{
    /// <summary>Writes the content under a new name, taking its SHA-256 from the bytes written.</summary>
    Task<StoredEvidenceFile> SaveAsync(int incidentId, Stream content, string extension, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored file for reading, or returns null when it is missing.</summary>
    Stream? OpenRead(string path);
}
