using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Risks;

public enum RiskTreatment
{
    Mitigate = 0,
    Avoid = 1,
    Accept = 2,
    Transfer = 3
}

/// <summary>Three steps (decided 2 Nov 2026); "overdue" is worked out from the target date, not stored.</summary>
public enum RiskStatus
{
    Open = 0,
    Treating = 1,
    Done = 2
}

/// <summary>
/// One information security risk (ISO 27001 6.1.2, 6.1.3, 8.2, 8.3), replacing a row of FNRC's Excel register.
/// The security team (risk.manage) and approvers (risk.approve) see every risk; an owner also sees their own.
/// Scores are worked out from the ratings (RiskScore), never typed.
/// </summary>
public class Risk : BaseEntity, IRestrictedRecord
{
    /// <summary>Shown as R-001; imported risks keep their number from the Excel.</summary>
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Threat { get; set; }
    public string? Vulnerability { get; set; }
    public string? ExistingControls { get; set; }

    // Ratings 1-5, as in the Excel's assessment criteria
    public int ImpactConfidentiality { get; set; }
    public int ImpactIntegrity { get; set; }
    public int ImpactAvailability { get; set; }
    public int Likelihood { get; set; }

    public RiskTreatment Treatment { get; set; }
    public string? RecommendedControls { get; set; }
    /// <summary>ISO 27001:2022 Annex A codes, comma-separated, e.g. "5.23,8.13" (see AnnexAControls).</summary>
    public string? AnnexAControls { get; set; }
    public int? OwnerUserId { get; set; }
    public DateOnly? TargetDate { get; set; }
    public RiskStatus Status { get; set; }

    // Expected ratings after treatment; optional
    public int? ResidualConfidentiality { get; set; }
    public int? ResidualIntegrity { get; set; }
    public int? ResidualAvailability { get; set; }
    public int? ResidualLikelihood { get; set; }

    /// <summary>The one approval ISO 27001 6.1.3 f asks for: the treatment plan, or accepting the risk. A change to the plan clears it.</summary>
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedByUserId { get; set; }

    public DateTime? LastReviewedAt { get; set; }
    public int? LastReviewedByUserId { get; set; }

    // Reminders already sent for the current target date; cleared when the date changes
    public DateTime? DueReminderSentAt { get; set; }
    public DateTime? OverdueReminderSentAt { get; set; }

    public virtual ICollection<RiskHistory> History { get; set; } = new List<RiskHistory>();
}

/// <summary>Every change to a risk, its approval and its reviews, with who and when. It can never be edited or deleted.</summary>
public class RiskHistory : BaseEntity, IRestrictedRecord, IAppendOnly
{
    public int RiskId { get; set; }
    public int UserId { get; set; }
    /// <summary>Created, Imported, Updated, Approved, Reviewed (see RiskActions).</summary>
    public string Kind { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Note { get; set; }

    public virtual Risk? Risk { get; set; }
}
