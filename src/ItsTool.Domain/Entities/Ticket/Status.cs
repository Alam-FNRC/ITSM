using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Ticket;

public class Status : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public bool IsClosedStatus { get; set; } = false;
    public string? ColorHex { get; set; }
    public int SortOrder { get; set; }
    public bool IsSystemDefault { get; set; }
    public bool PausesSla { get; set; }
    /// <summary>The ticket is finished (Closed). A closed status that is not final, such as Resolved, still waits for the requester.</summary>
    public bool IsFinal { get; set; }
}
