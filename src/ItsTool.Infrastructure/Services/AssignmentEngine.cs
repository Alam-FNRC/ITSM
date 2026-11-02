using System.Linq;
using System.Threading.Tasks;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

public class AssignmentEngine : IAssignmentEngine
{
    private readonly ItsToolDbContext _context;

    public AssignmentEngine(ItsToolDbContext context)
    {
        _context = context;
    }

    public async Task AssignTicketAsync(Ticket ticket)
    {
        var rules = await _context.AssignmentRules
            .Where(r => r.IsActive && !r.IsDeleted)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();

        var rule = rules.FirstOrDefault(r => IsRuleMatch(r, ticket));
        if (rule != null)
        {
            Assign(ticket, rule.TargetGroupId, rule.TargetUserId);
            return;
        }

        // No rule applies: the category's team, or else the team that owns the category's service (service catalogue)
        var defaults = await _context.Categories.AsNoTracking()
            .Where(c => c.Id == ticket.CategoryId)
            .Select(c => new { c.DefaultAssigneeGroupId, ServiceOwner = c.Service != null && !c.Service.IsDeleted ? c.Service.OwnerGroupId : null })
            .FirstOrDefaultAsync();
        var groupId = defaults?.DefaultAssigneeGroupId ?? defaults?.ServiceOwner;
        if (groupId.HasValue && await _context.Groups.AnyAsync(g => g.Id == groupId && !g.IsDeleted))
            Assign(ticket, groupId, null);
    }

    private void Assign(Ticket ticket, int? groupId, int? userId)
    {
        if (groupId.HasValue)
        {
            var groupAssignment = new TicketAssignment
            {
                TicketId = ticket.Id,
                AssignedGroupId = groupId,
                AssignedByUserId = ticket.RequesterUserId,
                IsActive = true
            };
            ticket.Assignments.Add(groupAssignment);
            _context.Set<TicketAssignment>().Add(groupAssignment);
        }

        if (userId.HasValue)
        {
            var userAssignment = new TicketAssignment
            {
                TicketId = ticket.Id,
                AssignedUserId = userId,
                AssignedByUserId = ticket.RequesterUserId,
                IsActive = true
            };
            ticket.Assignments.Add(userAssignment);
            _context.Set<TicketAssignment>().Add(userAssignment);
        }

        // An automatic assignment changes ticket data like a manual one does, so it is recorded the same way
        if (groupId.HasValue || userId.HasValue)
        {
            _context.TicketHistories.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                Action = "Assigned",
                FieldName = "Assignments",
                OldValue = "-",
                NewValue = $"Users:{userId},Groups:{groupId}",
                CreatedBy = "System"
            });
        }
    }

    internal static bool IsRuleMatch(ItsTool.Domain.Entities.Organization.AssignmentRule rule, Ticket ticket)
    {
        if (rule.ProjectId.HasValue && rule.ProjectId.Value != ticket.ProjectId) return false;
        if (rule.CategoryId.HasValue && rule.CategoryId.Value != ticket.CategoryId) return false;
        if (rule.TicketTypeId.HasValue && rule.TicketTypeId.Value != ticket.TypeId) return false;
        if (rule.PriorityId.HasValue && rule.PriorityId.Value != ticket.PriorityId) return false;
        return true;
    }
}
