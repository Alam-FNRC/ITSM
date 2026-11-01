namespace ItsTool.Infrastructure.Services;

/// <summary>
/// Which evidence files may be uploaded: the attachment types plus e-mails, Windows event logs and packet captures.
/// Evidence is always served as a download, never shown in the browser, so the served type does not matter here;
/// the first bytes must still match the extension.
/// </summary>
public static class EvidencePolicy
{
    // Within Kestrel's default 30 MB request limit
    public const long MaxBytes = 25 * 1024 * 1024;

    private static readonly byte[] OldOffice = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] PcapNg = { 0x0A, 0x0D, 0x0D, 0x0A };
    // Microsecond and nanosecond captures, in either byte order
    private static readonly byte[][] Pcap =
    {
        new byte[] { 0xD4, 0xC3, 0xB2, 0xA1 }, new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 },
        new byte[] { 0x4D, 0x3C, 0xB2, 0xA1 }, new byte[] { 0xA1, 0xB2, 0x3C, 0x4D }
    };

    private static readonly HashSet<string> ExtraTypes = new(StringComparer.OrdinalIgnoreCase) { ".eml", ".msg", ".evtx", ".pcap", ".pcapng" };

    public static bool IsAllowed(string fileName) =>
        UploadPolicy.ContentTypeFor(fileName) != null || ExtraTypes.Contains(Path.GetExtension(fileName));

    public static bool HasExpectedSignature(string fileName, ReadOnlySpan<byte> header)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".eml": return true;
            case ".msg": return header.StartsWith(OldOffice);
            case ".evtx": return header.StartsWith("ElfFile\0"u8);
            case ".pcapng": return header.StartsWith(PcapNg);
            case ".pcap":
                foreach (var magic in Pcap)
                {
                    if (header.StartsWith(magic)) return true;
                }
                return false;
            default: return UploadPolicy.HasExpectedSignature(fileName, header);
        }
    }
}
