using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Security;

public enum SecurityEvidenceKind
{
    LogExport = 0,
    Screenshot = 1,
    Email = 2,
    Document = 3,
    Device = 4,
    Other = 5
}

/// <summary>
/// Evidence for an incident (ISO 27001 A.5.28): what it is, who collected it and when, and either a file kept here
/// with the SHA-256 fingerprint taken on upload, or an item kept elsewhere, such as a seized laptop. Evidence is
/// never changed, deleted or replaced; who holds it and where it is come from its custody entries.
/// </summary>
public class SecurityEvidence : BaseEntity, IRestrictedRecord, IAppendOnly
{
    public int IncidentId { get; set; }
    /// <summary>Numbered per incident and shown as E1, E2...</summary>
    public int Number { get; set; }
    public SecurityEvidenceKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public int CollectedByUserId { get; set; }
    public DateTime CollectedAt { get; set; }
    public int RecordedByUserId { get; set; }

    // Set for a file kept here, empty for an item kept elsewhere
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    /// <summary>Lower-case hex SHA-256 of the file as uploaded.</summary>
    public string? Sha256 { get; set; }

    public virtual SecurityIncident? Incident { get; set; }
    public virtual ICollection<SecurityEvidenceCustody> Custody { get; set; } = new List<SecurityEvidenceCustody>();
}

public enum SecurityCustodyKind
{
    /// <summary>Recorded as evidence; names who holds it and where it is kept.</summary>
    Collected = 0,
    /// <summary>An item kept elsewhere passed to someone else or moved.</summary>
    HandedOver = 1,
    /// <summary>A copy of the file was downloaded after its fingerprint was checked.</summary>
    Downloaded = 2,
    /// <summary>The file's fingerprint was checked and still matched.</summary>
    Verified = 3,
    /// <summary>The file was missing or its fingerprint no longer matched.</summary>
    IntegrityFailed = 4
}

/// <summary>One step in an evidence item's chain of custody: who did what with it, and when.</summary>
public class SecurityEvidenceCustody : BaseEntity, IRestrictedRecord, IAppendOnly
{
    public int EvidenceId { get; set; }
    public SecurityCustodyKind Kind { get; set; }
    public int UserId { get; set; }
    /// <summary>Who holds the item after a Collected or HandedOver step.</summary>
    public int? CustodianUserId { get; set; }
    /// <summary>Where the item is kept after a Collected or HandedOver step.</summary>
    public string? Location { get; set; }
    public string? Note { get; set; }
    /// <summary>The fingerprint found when the file was checked or downloaded.</summary>
    public string? Sha256 { get; set; }

    public virtual SecurityEvidence? Evidence { get; set; }
}
