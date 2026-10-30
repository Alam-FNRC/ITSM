using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Security;

/// <summary>
/// The incident's own history: every action taken and every change, with who and when (A.5.26). It replaces the
/// system audit log for incidents and can never be edited or deleted.
/// </summary>
public class SecurityIncidentAction : BaseEntity, IRestrictedRecord, IAppendOnly
{
    public int IncidentId { get; set; }
    public int UserId { get; set; }
    /// <summary>What happened, e.g. Reported, Note, StatusChanged, Triaged, MemberAdded (see SecurityIncidentActions).</summary>
    public string Kind { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Note { get; set; }

    public virtual SecurityIncident? Incident { get; set; }
}

/// <summary>Someone added to one incident, such as the server admin who must apply a fix; they see that incident only.</summary>
public class SecurityIncidentMember : BaseEntity, IRestrictedRecord
{
    public int IncidentId { get; set; }
    public int UserId { get; set; }
    public int AddedByUserId { get; set; }

    public virtual SecurityIncident? Incident { get; set; }
}

/// <summary>Who opened an incident and when.</summary>
public class SecurityIncidentAccess : BaseEntity, IRestrictedRecord, IAppendOnly
{
    public int IncidentId { get; set; }
    public int UserId { get; set; }
}

public enum SecurityAffectedItemKind
{
    Server = 0,
    Workstation = 1,
    NetworkDevice = 2,
    Address = 3,
    Application = 4,
    Service = 5,
    UserAccount = 6,
    Other = 7
}

public enum SecurityAffectedItemStatus
{
    Open = 0,
    Fixed = 1,
    FalsePositive = 2,
    AcceptedRisk = 3
}

/// <summary>A server, address, device, account or service an incident affects; each one is ticked off when done.</summary>
public class SecurityAffectedItem : BaseEntity, IRestrictedRecord
{
    public int IncidentId { get; set; }
    public SecurityAffectedItemKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public SecurityAffectedItemStatus Status { get; set; }
    /// <summary>Required for a false positive or an accepted risk.</summary>
    public string? StatusReason { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public int? StatusChangedByUserId { get; set; }

    public virtual SecurityIncident? Incident { get; set; }
}
