using System.Security.Cryptography;
using System.Text;
using ItsTool.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace ItsTool.UnitTests.Services;

public class LocalEvidenceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"itsm-evidence-{Guid.NewGuid():N}");
    private readonly LocalEvidenceStore _store;

    public LocalEvidenceStoreTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [LocalEvidenceStore.PathSetting] = _root + Path.DirectorySeparatorChar })
            .Build();
        _store = new LocalEvidenceStore(configuration);
    }

    [Fact]
    public async Task A_file_is_kept_under_a_new_name_with_the_fingerprint_of_what_was_written()
    {
        var bytes = Encoding.UTF8.GetBytes("4625 failed logon for administrator");

        var stored = await _store.SaveAsync(7, new MemoryStream(bytes), ".LOG");

        Assert.Equal(Path.Combine(_root, "7"), Path.GetDirectoryName(stored.Path));
        Assert.EndsWith(".log", stored.Path);
        Assert.Equal(bytes.Length, stored.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.Sha256);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(stored.Path));

        var second = await _store.SaveAsync(7, new MemoryStream(bytes), ".log");
        Assert.NotEqual(stored.Path, second.Path);
    }

    [Fact]
    public async Task Only_files_inside_the_evidence_folder_are_opened()
    {
        var stored = await _store.SaveAsync(1, new MemoryStream(new byte[] { 1, 2, 3 }), ".txt");
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "not evidence");
        try
        {
            using (var content = _store.OpenRead(stored.Path)) Assert.NotNull(content);
            Assert.Null(_store.OpenRead(outside));
            Assert.Null(_store.OpenRead(Path.Combine(_root, "1", "..", "..", Path.GetFileName(outside))));
            // A sibling folder whose name merely starts with the evidence folder's
            Assert.Null(_store.OpenRead(_root + "-other" + Path.DirectorySeparatorChar + "a.txt"));
            Assert.Null(_store.OpenRead(Path.Combine(_root, "1", "missing.txt")));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}

public class EvidencePolicyTests
{
    [Theory]
    [InlineData("phish.eml")]
    [InlineData("phish.msg")]
    [InlineData("Security.evtx")]
    [InlineData("capture.pcap")]
    [InlineData("capture.pcapng")]
    [InlineData("export.csv")]
    [InlineData("samples.zip")]
    public void Mail_event_logs_captures_and_attachment_types_are_allowed(string fileName)
    {
        Assert.True(EvidencePolicy.IsAllowed(fileName));
    }

    [Theory]
    [InlineData("dropper.exe")]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("noextension")]
    public void Other_types_are_refused(string fileName)
    {
        Assert.False(EvidencePolicy.IsAllowed(fileName));
    }

    [Theory]
    [InlineData("Security.evtx", new byte[] { 0x45, 0x6C, 0x66, 0x46, 0x69, 0x6C, 0x65, 0x00 }, true)]
    [InlineData("Security.evtx", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, false)]
    [InlineData("phish.msg", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, true)]
    [InlineData("phish.msg", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, false)]
    [InlineData("capture.pcap", new byte[] { 0xD4, 0xC3, 0xB2, 0xA1 }, true)]
    [InlineData("capture.pcap", new byte[] { 0xA1, 0xB2, 0x3C, 0x4D }, true)]
    [InlineData("capture.pcap", new byte[] { 0x0A, 0x0D, 0x0D, 0x0A }, false)]
    [InlineData("capture.pcapng", new byte[] { 0x0A, 0x0D, 0x0D, 0x0A }, true)]
    [InlineData("phish.eml", new byte[] { 0x52, 0x65, 0x63 }, true)]
    [InlineData("report.pdf", new byte[] { 0x4D, 0x5A }, false)]
    public void The_first_bytes_must_match_the_extension(string fileName, byte[] header, bool expected)
    {
        Assert.Equal(expected, EvidencePolicy.HasExpectedSignature(fileName, header));
    }
}
