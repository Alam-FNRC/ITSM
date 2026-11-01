using System.Security.Cryptography;
using System.Text;
using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Security;
using ItsTool.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ItsTool.UnitTests.Services;

/// <summary>Evidence (A.5.28): fingerprinted files, items kept elsewhere, chain of custody and integrity checks.</summary>
public class SecurityEvidenceServiceTests : TestBase
{
    private const int Reporter = 1, Team = 2, Member = 3, Colleague = 5;
    private static readonly byte[] EventLog = Encoding.UTF8.GetBytes("4625 An account failed to log on: administrator");
    private readonly MemoryEvidenceStore _store = new();
    private readonly SecurityIncidentService _service;

    public SecurityEvidenceServiceTests()
    {
        var permissions = new Mock<IPermissionCalculator>();
        permissions.Setup(p => p.CalculateEffectivePermissionsAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) => userId == Team
                ? new HashSet<string> { PermissionConstants.SecurityIncidentView, PermissionConstants.SecurityIncidentManage }
                : new HashSet<string>());
        _service = new SecurityIncidentService(_context, permissions.Object, _store);
        _context.Users.Add(new User { Id = Colleague, Username = "omar", Email = "omar@fnrc.test", FirstName = "Omar", LastName = "Ali" });
        _context.SaveChanges();
    }

    private async Task<int> IncidentAsync()
    {
        var report = await _service.ReportAsync(Reporter, new ReportSecurityIssueDto("Odd logons", "Many failures", null));
        var id = await _context.SecurityIncidents.Where(i => i.Reference == report.Reference).Select(i => i.Id).SingleAsync();
        _context.SecurityIncidentMembers.Add(new SecurityIncidentMember { IncidentId = id, UserId = Member, AddedByUserId = Team });
        await _context.SaveChangesAsync();
        return id;
    }

    private static IFormFile Upload(string name, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);

    private Task<SecurityEvidenceDto> AddLogAsync(int incidentId, int userId = Team) =>
        _service.AddEvidenceAsync(userId, incidentId, new AddSecurityEvidenceDto("LogExport", "Security log from fs01", null, null, null), Upload("Security.log", EventLog));

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private Task<List<SecurityEvidenceCustody>> CustodyAsync(int evidenceId) =>
        _context.SecurityEvidenceCustody.AsNoTracking().Where(c => c.EvidenceId == evidenceId).OrderBy(c => c.Id).ToListAsync();

    [Fact]
    public async Task Files_are_fingerprinted_and_items_kept_elsewhere_are_held_by_their_collector()
    {
        var id = await IncidentAsync();

        var log = await AddLogAsync(id);
        var laptop = await _service.AddEvidenceAsync(Team, id,
            new AddSecurityEvidenceDto("Device", "HR laptop, serial 5CD123", Colleague, DateTime.UtcNow.AddHours(-2), "Evidence safe, room 2.14"), null);

        Assert.Equal((1, Sha256(EventLog), (long?)EventLog.Length, "Security.log"), (log.Number, log.Sha256, log.FileSize, log.FileName));
        var collected = Assert.Single(log.Custody);
        Assert.Equal(("Collected", (int?)null, Sha256(EventLog)), (collected.Kind, collected.CustodianUserId, collected.Sha256));

        Assert.Equal((2, (string?)null, Colleague, (int?)Colleague, "Evidence safe, room 2.14"),
            (laptop.Number, laptop.Sha256, laptop.CollectedByUserId, laptop.CustodianUserId, laptop.Location));
        Assert.Equal(new[] { "1", "2" }, await _context.SecurityIncidentActions
            .Where(a => a.IncidentId == id && a.Kind == SecurityIncidentActions.EvidenceAdded).OrderBy(a => a.Id).Select(a => a.NewValue).ToListAsync());

        var detail = await _service.GetAsync(Team, id);
        Assert.Equal(new[] { 1, 2 }, detail.Evidence.Select(e => e.Number));
        Assert.Equal("Omar Ali", detail.UserNames[Colleague]);
    }

    [Theory]
    [InlineData("dropper.exe", "MZ")]
    [InlineData("invoice.pdf", "MZ")]
    [InlineData("empty.log", "")]
    public async Task Refused_files_leave_nothing_behind(string fileName, string content)
    {
        var id = await IncidentAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddEvidenceAsync(Team, id,
            new AddSecurityEvidenceDto("Other", "Attachment from the phishing mail", null, null, null), Upload(fileName, Encoding.ASCII.GetBytes(content))));

        Assert.Empty(_store.Files);
        Assert.False(await _context.SecurityEvidence.AnyAsync());
    }

    [Fact]
    public async Task Evidence_needs_a_file_or_a_place_and_cannot_come_from_the_future()
    {
        var id = await IncidentAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddEvidenceAsync(Team, id, new AddSecurityEvidenceDto("Device", "USB stick", null, null, " "), null));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddEvidenceAsync(Team, id,
            new AddSecurityEvidenceDto("Device", "USB stick", null, DateTime.UtcNow.AddDays(1), "Safe"), null));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddEvidenceAsync(Team, id, new AddSecurityEvidenceDto("Laptop", "USB stick", null, null, "Safe"), null));
    }

    [Fact]
    public async Task People_on_the_incident_add_evidence_but_only_the_security_team_records_hand_overs()
    {
        var id = await IncidentAsync();
        var disk = await _service.AddEvidenceAsync(Member, id, new AddSecurityEvidenceDto("Device", "fs01 system disk", null, null, "Server room rack 3"), null);
        Assert.Equal(Member, disk.CustodianUserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.HandOverEvidenceAsync(Member, id, disk.Id, new HandOverSecurityEvidenceDto(Colleague, "Desk", null)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => AddLogAsync(id, Reporter));

        await _service.HandOverEvidenceAsync(Team, id, disk.Id, new HandOverSecurityEvidenceDto(Colleague, "Forensics lab", "Sealed bag 0042"));

        var evidence = (await _service.GetAsync(Team, id)).Evidence.Single();
        Assert.Equal(((int?)Colleague, "Forensics lab"), (evidence.CustodianUserId, evidence.Location));
        Assert.Equal(new[] { "Collected", "HandedOver" }, evidence.Custody.Select(c => c.Kind));
        Assert.True(await _context.SecurityIncidentActions.AnyAsync(a =>
            a.Kind == SecurityIncidentActions.EvidenceHandedOver && a.OldValue == "1" && a.NewValue == Colleague.ToString()));
    }

    [Fact]
    public async Task A_file_kept_here_is_not_handed_over()
    {
        var id = await IncidentAsync();
        var log = await AddLogAsync(id);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.HandOverEvidenceAsync(Team, id, log.Id, new HandOverSecurityEvidenceDto(Colleague, "USB", null)));
    }

    [Fact]
    public async Task Every_download_is_checked_and_recorded()
    {
        var id = await IncidentAsync();
        var log = await AddLogAsync(id);

        var file = await _service.OpenEvidenceAsync(Member, id, log.Id);
        using (var copy = new MemoryStream())
        {
            await file.Content.CopyToAsync(copy);
            file.Content.Dispose();
            Assert.Equal(EventLog, copy.ToArray());
        }
        Assert.Equal("Security.log", file.FileName);

        var check = await _service.CheckEvidenceAsync(Team, id, log.Id);
        Assert.True(check.Intact);
        Assert.Equal(new[] { SecurityCustodyKind.Collected, SecurityCustodyKind.Downloaded, SecurityCustodyKind.Verified },
            (await CustodyAsync(log.Id)).Select(c => c.Kind));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.OpenEvidenceAsync(Reporter, id, log.Id));
    }

    [Fact]
    public async Task A_changed_file_is_not_sent_and_the_failure_is_recorded()
    {
        var id = await IncidentAsync();
        var log = await AddLogAsync(id);
        var stored = _store.Files.Single();
        stored.Value[0] ^= 0xFF;

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.OpenEvidenceAsync(Team, id, log.Id));
        var check = await _service.CheckEvidenceAsync(Team, id, log.Id);

        Assert.False(check.Intact);
        Assert.Equal(Sha256(stored.Value), check.Found);
        var custody = await CustodyAsync(log.Id);
        Assert.Equal(new[] { SecurityCustodyKind.Collected, SecurityCustodyKind.IntegrityFailed, SecurityCustodyKind.IntegrityFailed }, custody.Select(c => c.Kind));
        Assert.DoesNotContain(custody, c => c.Kind == SecurityCustodyKind.Downloaded);
        Assert.Equal(2, await _context.SecurityIncidentActions.CountAsync(a => a.Kind == SecurityIncidentActions.EvidenceIntegrityFailed && a.NewValue == "1"));

        // The stored fingerprint is the one taken on upload, whatever happened to the file since
        Assert.Equal(Sha256(EventLog), (await _service.GetAsync(Team, id)).Evidence.Single().Sha256);
    }

    [Fact]
    public async Task A_missing_file_fails_its_check()
    {
        var id = await IncidentAsync();
        var log = await AddLogAsync(id);
        _store.Files.Clear();

        var check = await _service.CheckEvidenceAsync(Team, id, log.Id);

        Assert.Equal((false, (string?)null), (check.Intact, check.Found));
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.OpenEvidenceAsync(Team, id, log.Id));
    }

    private sealed class MemoryEvidenceStore : IEvidenceStore
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredEvidenceFile> SaveAsync(int incidentId, Stream content, string extension, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var path = $"{incidentId}/{Guid.NewGuid():N}{extension}";
            Files[path] = copy.ToArray();
            return new StoredEvidenceFile(path, copy.Length, Sha256(copy.ToArray()));
        }

        public Stream? OpenRead(string path) => Files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
    }
}
