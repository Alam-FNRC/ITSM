using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Ticket;

/// <summary>
/// Times measured on a ticket for the service-level KPIs, in working minutes of the business calendar.
/// Worked out again from the ticket's history after every change, never entered by hand.
/// </summary>
public class TicketTimeMeasure : BaseEntity
{
    public int TicketId { get; set; }

    /// <summary>First action by an assigned agent or a member of an assigned group.</summary>
    public DateTime? OwnedAt { get; set; }
    public int? OwnedByUserId { get; set; }
    public int? TimeToOwnMinutes { get; set; }

    /// <summary>Time spent in statuses that pause the SLA, up to the last status change.</summary>
    public int WaitingMinutes { get; set; }
    /// <summary>Set while the ticket is in such a status; the time since then is not in WaitingMinutes yet.</summary>
    public DateTime? WaitingSince { get; set; }

    /// <summary>When the ticket last moved from an open status to a closed one; cleared when it is reopened.</summary>
    public DateTime? ResolvedAt { get; set; }
    /// <summary>From opening to ResolvedAt, minus the waiting time before it.</summary>
    public int? TimeToResolveMinutes { get; set; }

    public int ReopenCount { get; set; }
    /// <summary>Times the ticket was assigned or transferred to a group, including by assignment rules.</summary>
    public int AssignmentCount { get; set; }

    public DateTime CalculatedAt { get; set; }

    public virtual Ticket? Ticket { get; set; }
}
