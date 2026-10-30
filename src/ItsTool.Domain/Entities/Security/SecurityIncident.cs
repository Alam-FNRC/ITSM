using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Security;

public enum SecurityIncidentStatus
{
    New = 0,
    Triage = 1,
    Containment = 2,
    Eradication = 3,
    Recovery = 4,
    Review = 5,
    Closed = 6
}

/// <summary>The security team's decision on a report (ISO 27001 A.5.25).</summary>
public enum SecurityTriageOutcome
{
    Pending = 0,
    NotSecurity = 1,
    Event = 2,
    Incident = 3
}

public enum SecurityIncidentCategory
{
    Unclassified = 0,
    Phishing = 1,
    Malware = 2,
    UnauthorisedAccess = 3,
    AccountCompromise = 4,
    DataLeak = 5,
    LostDevice = 6,
    DenialOfService = 7,
    PolicyViolation = 8,
    Vulnerability = 9,
    Other = 10
}

public enum SecurityIncidentSource
{
    StaffReport = 0,
    SecurityTeam = 1,
    FederalReport = 2,
    Ticket = 3
}

/// <summary>
/// A suspected or confirmed information security incident (feature spec, "Security incidents"). Only holders of
/// security.incident.view and the people added to the incident can see it.
/// </summary>
public class SecurityIncident : BaseEntity, IRestrictedRecord
{
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SecurityIncidentSource Source { get; set; }
    public SecurityIncidentStatus Status { get; set; }
    public int ReporterUserId { get; set; }
    /// <summary>When it happened, if the reporter knows.</summary>
    public DateTime? OccurredAt { get; set; }
    public int? HandlerUserId { get; set; }

    public SecurityTriageOutcome TriageOutcome { get; set; }
    public string? TriageReason { get; set; }
    public DateTime? TriagedAt { get; set; }
    public int? TriagedByUserId { get; set; }

    public SecurityIncidentCategory Category { get; set; }
    /// <summary>1 (critical) to 4 (low), from the impact on confidentiality, integrity and availability.</summary>
    public int? Severity { get; set; }
    public bool PersonalDataInvolved { get; set; }

    // First time each phase was reached, for the KPIs
    public DateTime? ContainedAt { get; set; }
    public DateTime? EradicatedAt { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Post-incident review (A.5.27), required before closing a severity 1 or 2 incident
    public string? ReviewRootCause { get; set; }
    public string? ReviewWhatWorked { get; set; }
    public string? ReviewImprovements { get; set; }
    public string? ReviewFollowUps { get; set; }
    public DateTime? ReviewCompletedAt { get; set; }
    public int? ReviewCompletedByUserId { get; set; }

    public virtual ICollection<SecurityIncidentAction> Actions { get; set; } = new List<SecurityIncidentAction>();
    public virtual ICollection<SecurityIncidentMember> Members { get; set; } = new List<SecurityIncidentMember>();
    public virtual ICollection<SecurityAffectedItem> AffectedItems { get; set; } = new List<SecurityAffectedItem>();
}
