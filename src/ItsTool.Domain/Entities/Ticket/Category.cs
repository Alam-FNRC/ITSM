using ItsTool.Domain.Common;

namespace ItsTool.Domain.Entities.Ticket;

public class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int ProjectId { get; set; }
    public int? ParentCategoryId { get; set; }
    public string? Description { get; set; }
    /// <summary>The team that gets new tickets in this category when no assignment rule applies.</summary>
    public int? DefaultAssigneeGroupId { get; set; }
    /// <summary>The service in the catalogue this category belongs to.</summary>
    public int? ServiceId { get; set; }
    /// <summary>The ticket type the form picks for this category, e.g. Service Request for "New laptop".</summary>
    public int? DefaultTypeId { get; set; }

    public virtual Catalog.ItService? Service { get; set; }
    public virtual TicketType? DefaultType { get; set; }
}
