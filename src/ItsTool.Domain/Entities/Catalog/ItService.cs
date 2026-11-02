using ItsTool.Domain.Common;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Ticket;

namespace ItsTool.Domain.Entities.Catalog;

/// <summary>
/// An IT service offered to staff, such as Email or Network (feature spec, "Service catalogue"). Ticket categories sit
/// under a service, so a ticket's category also says which service it is about. Family only groups services on the
/// ticket form; the owner team gets new tickets for the service when no assignment rule or category team applies.
/// </summary>
public class ItService : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Family { get; set; }
    public string? Description { get; set; }
    public int? OwnerGroupId { get; set; }
    public int SortOrder { get; set; }

    public virtual Group? OwnerGroup { get; set; }
    public virtual ICollection<Category> Categories { get; set; } = new List<Category>();
}
