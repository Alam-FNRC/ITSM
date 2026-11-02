using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

/// <summary>Loads one row per ticket for <see cref="KpiCalculator"/>, which does the counting.</summary>
public class KpiService : IKpiService
{
    private readonly ItsToolDbContext _context;
    private readonly IConfiguration? _config;

    public KpiService(ItsToolDbContext context, IConfiguration? config = null)
    {
        _context = context;
        _config = config;
    }

    public async Task<KpiReportDto> GetReportAsync(KpiFilterDto filter)
    {
        var now = DateTime.UtcNow;
        var period = KpiCalculator.ResolvePeriod(filter.From, filter.To, now, BusinessCalendar.ResolveTimeZone(_config?[BusinessCalendar.TimeZoneSetting]));
        var (from, to) = (period.FromUtc, period.ToUtc);
        var tickets = Filter(_context.Tickets.AsNoTracking().Where(t => !t.IsDeleted), filter);

        // Tickets open now (for the backlog) and tickets with anything in the period
        var loaded = await tickets
            .Where(t => !t.Status!.IsClosedStatus
                || (t.CreatedAt >= from && t.CreatedAt < to)
                || (t.TimeMeasure != null && (
                    (t.TimeMeasure.OwnedAt >= from && t.TimeMeasure.OwnedAt < to)
                    || (t.TimeMeasure.ResolvedAt >= from && t.TimeMeasure.ResolvedAt < to)
                    || (t.TimeMeasure.ClosedAt >= from && t.TimeMeasure.ClosedAt < to))))
            .Select(t => new
            {
                t.Id,
                t.CreatedAt,
                IsOpen = !t.Status!.IsClosedStatus,
                DepartmentId = t.RequesterUser != null ? t.RequesterUser.DepartmentId : null,
                t.CategoryId,
                t.PriorityId,
                GroupIds = t.Assignments.Where(a => a.IsActive && !a.IsDeleted && a.AssignedGroupId != null).Select(a => a.AssignedGroupId!.Value).ToList(),
                AgentIds = t.Assignments.Where(a => a.IsActive && !a.IsDeleted && a.AssignedUserId != null).Select(a => a.AssignedUserId!.Value).ToList(),
                Times = t.TimeMeasure,
                Sla = t.TicketSla
            })
            .AsSplitQuery()
            .ToListAsync();

        var rows = loaded.Select(t => new KpiCalculator.TicketRow(
            t.Id, t.CreatedAt, t.IsOpen, t.DepartmentId, t.CategoryId, t.PriorityId, t.GroupIds, t.AgentIds,
            t.Times?.OwnedAt, t.Times?.TimeToOwnMinutes,
            t.Times?.ResolvedAt, t.Times?.TimeToResolveMinutes,
            t.Times?.ClosedAt, t.Times?.TimeToCloseMinutes,
            t.Times?.ReopenCount ?? 0, t.Times?.AssignmentCount ?? 0,
            t.Sla?.FirstResponseDueAt, t.Sla?.FirstResponseMetAt, t.Sla?.FirstResponseBreached ?? false,
            t.Sla?.ResolutionDueAt, t.Sla?.ResolutionBreached ?? false, t.Sla?.PausedAt != null)).ToList();

        var ticketIds = tickets.Select(t => t.Id);
        var ratings = await _context.TicketSurveys.AsNoTracking()
            .Where(s => !s.IsDeleted && s.SubmittedAt >= from && s.SubmittedAt < to && ticketIds.Contains(s.TicketId))
            .Select(s => s.Rating)
            .ToListAsync();

        return KpiCalculator.Calculate(rows, ratings, period, now, await LoadNamesAsync(rows));
    }

    public async Task<KpiFilterOptionsDto> GetFilterOptionsAsync()
    {
        static List<KpiOptionDto> Sorted(IEnumerable<KpiOptionDto> options) => options.OrderBy(o => o.Name, StringComparer.CurrentCulture).ToList();

        var departments = await _context.Departments.AsNoTracking().Where(d => !d.IsDeleted).Select(d => new KpiOptionDto(d.Id, d.Name)).ToListAsync();
        var categories = await _context.Categories.AsNoTracking().Where(c => !c.IsDeleted).Select(c => new KpiOptionDto(c.Id, c.Name)).ToListAsync();
        var priorities = await _context.Priorities.AsNoTracking().Where(p => !p.IsDeleted)
            .OrderBy(p => p.SeverityLevel).ThenBy(p => p.SortOrder).Select(p => new KpiOptionDto(p.Id, p.Name)).ToListAsync();
        var teams = await _context.Groups.AsNoTracking().Where(g => !g.IsDeleted).Select(g => new KpiOptionDto(g.Id, g.Name)).ToListAsync();
        // Agents are the members of the support groups
        var agents = await _context.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && u.GroupMemberships.Any(m => !m.IsDeleted && !m.Group!.IsDeleted))
            .Select(u => new KpiOptionDto(u.Id, u.FirstName + " " + u.LastName))
            .ToListAsync();

        return new KpiFilterOptionsDto(Sorted(departments), Sorted(categories), priorities, Sorted(teams), Sorted(agents));
    }

    private static IQueryable<Ticket> Filter(IQueryable<Ticket> tickets, KpiFilterDto filter)
    {
        if (filter.DepartmentId.HasValue) tickets = tickets.Where(t => t.RequesterUser != null && t.RequesterUser.DepartmentId == filter.DepartmentId);
        if (filter.CategoryId.HasValue) tickets = tickets.Where(t => t.CategoryId == filter.CategoryId);
        if (filter.PriorityId.HasValue) tickets = tickets.Where(t => t.PriorityId == filter.PriorityId);
        if (filter.GroupId.HasValue)
            tickets = tickets.Where(t => t.Assignments.Any(a => a.IsActive && !a.IsDeleted && a.AssignedGroupId == filter.GroupId));
        if (filter.AgentUserId.HasValue)
            tickets = tickets.Where(t => t.Assignments.Any(a => a.IsActive && !a.IsDeleted && a.AssignedUserId == filter.AgentUserId));
        return tickets;
    }

    // Removed teams, people and categories keep their names in old reports
    private async Task<KpiCalculator.Names> LoadNamesAsync(List<KpiCalculator.TicketRow> rows)
    {
        var groupIds = rows.SelectMany(r => r.GroupIds).Distinct().ToList();
        var userIds = rows.SelectMany(r => r.AgentIds).Distinct().ToList();
        var categoryIds = rows.Select(r => r.CategoryId).Distinct().ToList();
        var departmentIds = rows.Where(r => r.DepartmentId.HasValue).Select(r => r.DepartmentId!.Value).Distinct().ToList();
        var priorityIds = rows.Select(r => r.PriorityId).Distinct().ToList();

        return new KpiCalculator.Names(
            await _context.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name),
            await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName),
            await _context.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name),
            await _context.Departments.AsNoTracking().Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name),
            await _context.Priorities.AsNoTracking().Where(p => priorityIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name));
    }
}
