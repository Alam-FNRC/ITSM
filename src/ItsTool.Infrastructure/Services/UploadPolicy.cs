namespace ItsTool.Infrastructure.Services;

/// <summary>
/// Which attachment types may be uploaded and how they are served (review A11). The served content type is decided
/// here from the extension, never taken from the client, so an upload cannot make the browser run it as a page.
/// </summary>
public static class UploadPolicy
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int HeaderLength = 16;

    private const string PlainText = "text/plain";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        // Text formats are served as plain text, so XML or JSON can never be rendered as a page
        [".txt"] = PlainText,
        [".md"] = PlainText,
        [".csv"] = PlainText,
        [".json"] = PlainText,
        [".xml"] = PlainText,
        [".log"] = PlainText,
        [".tex"] = PlainText,
        [".sql"] = PlainText,
        [".py"] = PlainText,
        [".zip"] = "application/zip",
        [".rar"] = "application/vnd.rar",
        [".7z"] = "application/x-7z-compressed",
        [".tar"] = "application/x-tar",
        // .tar.gz files end in .gz
        [".gz"] = "application/gzip"
        // No .svg: an SVG can carry script
    };

    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] EmptyZip = { 0x50, 0x4B, 0x05, 0x06 };
    private static readonly byte[] OldOffice = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] Gzip = { 0x1F, 0x8B };
    private static readonly byte[] SevenZip = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };

    /// <summary>The content type to serve for this file name, or null when the type may not be uploaded.</summary>
    public static string? ContentTypeFor(string fileName) =>
        ContentTypes.TryGetValue(Path.GetExtension(fileName), out var contentType) ? contentType : null;

    /// <summary>The name shown to users: the client's name without any folder part.</summary>
    public static string DisplayName(string fileName) => fileName.Split('/', '\\').Last().Trim();

    /// <summary>Whether the type has a fixed signature in its first bytes that <see cref="HasExpectedSignature"/> checks.</summary>
    public static bool HasSignature(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is not (".txt" or ".md" or ".csv" or ".json" or ".xml" or ".log" or ".tex" or ".sql" or ".py" or ".tar");

    public static bool HasExpectedSignature(string fileName, ReadOnlySpan<byte> header) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => header.StartsWith(Jpeg),
            ".png" => header.StartsWith(Png),
            ".gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
            ".webp" => header.Length >= 12 && header.StartsWith("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".pdf" => header.StartsWith("%PDF-"u8),
            ".docx" or ".xlsx" or ".zip" => header.StartsWith(Zip) || header.StartsWith(EmptyZip),
            ".doc" or ".xls" => header.StartsWith(OldOffice),
            ".gz" => header.StartsWith(Gzip),
            ".7z" => header.StartsWith(SevenZip),
            ".rar" => header.StartsWith("Rar!\x1A\x07"u8),
            _ => !HasSignature(fileName)
        };
}
