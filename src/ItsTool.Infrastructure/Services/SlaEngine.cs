using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.SLA;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Domain.Entities.Notification;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

public class SlaEngine : ISlaEngine
{
    private readonly ItsToolDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IConfiguration? _config;
    private BusinessCalendar? _calendar;

    public SlaEngine(ItsToolDbContext context, IEmailService emailService, INotificationDispatcher notificationDispatcher, IConfiguration? config = null)
    {
        _context = context;
        _emailService = emailService;
        _notificationDispatcher = notificationDispatcher;
        _config = config;
    }

    private async Task<BusinessCalendar> GetCalendarAsync() =>
        _calendar ??= await BusinessCalendar.LoadAsync(_context, _config?[BusinessCalendar.TimeZoneSetting]);

    private async Task<DateTime> CalculateDueTimeAsync(DateTime startTimeUtc, int minutesToAdd) =>
        (await GetCalendarAsync()).AddWorkingMinutes(startTimeUtc, minutesToAdd);

    public async Task AttachSlaToTicketAsync(int ticketId)
    {
        var ticket = await _context.Tickets.FindAsync(ticketId);
        if (ticket == null) return;

        var policy = await _context.SlaPolicies
            .Where(p => p.IsActive && !p.IsDeleted && (p.ProjectId == ticket.ProjectId || p.ProjectId == null))
            .OrderByDescending(p => p.ProjectId.HasValue)
            .FirstOrDefaultAsync();

        if (policy == null) return;

        var target = await _context.SlaTargets
            .Where(t => t.IsActive && !t.IsDeleted && t.SlaPolicyId == policy.Id && t.PriorityId == ticket.PriorityId && 
                        (t.TicketTypeId == ticket.TypeId || t.TicketTypeId == null))
            .OrderByDescending(t => t.TicketTypeId.HasValue)
            .FirstOrDefaultAsync();

        if (target == null) return;

        var now = DateTime.UtcNow;
        var firstResponseDue = await CalculateDueTimeAsync(now, target.FirstResponseMinutes);
        var resolutionDue = await CalculateDueTimeAsync(now, target.ResolutionMinutes);

        var sla = new TicketSla
        {
            TicketId = ticket.Id,
            FirstResponseDueAt = firstResponseDue,
            ResolutionDueAt = resolutionDue
        };

        _context.TicketSlas.Add(sla);
        await _context.SaveChangesAsync();
    }

    public async Task ProcessTicketStatusChangeAsync(int ticketId, int oldStatusId, int newStatusId)
    {
        var sla = await _context.TicketSlas.FirstOrDefaultAsync(s => s.TicketId == ticketId);
        if (sla == null) return;

        if (sla.FirstResponseMetAt == null)
            sla.FirstResponseMetAt = DateTime.UtcNow;

        var oldStatus = await _context.Statuses.FindAsync(oldStatusId);
        var newStatus = await _context.Statuses.FindAsync(newStatusId);
        if (oldStatus == null || newStatus == null) return;

        var now = DateTime.UtcNow;

        if (!oldStatus.PausesSla && newStatus.PausesSla)
        {
            ApplyPause(sla, now);
        }
        else if (oldStatus.PausesSla && !newStatus.PausesSla && sla.PausedAt.HasValue)
        {
            await ApplyResumeAsync(sla, now);
        }

        if (newStatus.IsClosedStatus && sla.ResolutionMetAt == null)
            sla.ResolutionMetAt = now;

        await _context.SaveChangesAsync();
    }

    private static void ApplyPause(TicketSla sla, DateTime now)
    {
        sla.PausedAt = now;
    }

    private async Task ApplyResumeAsync(TicketSla sla, DateTime now)
    {
        if (!sla.PausedAt.HasValue) return;

        var pausedDuration = now - sla.PausedAt.Value;
        sla.TotalPausedMinutes += (int)pausedDuration.TotalMinutes;

        var calendar = await GetCalendarAsync();
        sla.FirstResponseDueAt = MoveDeadline(calendar, sla.FirstResponseDueAt, sla.PausedAt.Value, now);
        sla.ResolutionDueAt = MoveDeadline(calendar, sla.ResolutionDueAt, sla.PausedAt.Value, now);

        sla.PausedAt = null;
    }

    // The working time that was left when the ticket was paused is given back from now. The old code gave back
    // the clock time instead, so a pause over a weekend added the whole weekend as working time.
    internal static DateTime? MoveDeadline(BusinessCalendar calendar, DateTime? dueAt, DateTime pausedAt, DateTime now)
    {
        if (!dueAt.HasValue || dueAt.Value <= pausedAt) return dueAt;
        return calendar.AddWorkingMinutes(now, calendar.WorkingMinutesBetween(pausedAt, dueAt.Value));
    }

    public async Task ProcessTicketCommentAsync(int ticketId, bool isInternal)
    {
        if (isInternal) return; // Only public comments count towards First Response

        var sla = await _context.TicketSlas.FirstOrDefaultAsync(s => s.TicketId == ticketId);
        if (sla != null && sla.FirstResponseMetAt == null)
        {
            sla.FirstResponseMetAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task CheckBreachesAsync(DateTime nowUtc)
    {
        var activeSlas = await _context.TicketSlas
            .Include(s => s.Ticket)
                .ThenInclude(t => t!.Assignments)
            .Where(s => s.PausedAt == null && (!s.ResolutionMetAt.HasValue || !s.FirstResponseMetAt.HasValue))
            .ToListAsync();

        foreach (var sla in activeSlas)
        {
            if (sla.Ticket == null) continue;
            var targetUserIds = sla.Ticket.Assignments
                .Where(a => a.IsActive && a.AssignedUserId.HasValue)
                .Select(a => a.AssignedUserId!.Value)
                .ToList();
            if (targetUserIds.Count == 0) targetUserIds.Add(sla.Ticket.RequesterUserId);

            await CheckFirstResponseAsync(sla, targetUserIds, nowUtc);
            await CheckResolutionAsync(sla, targetUserIds, nowUtc);
        }

        await _context.SaveChangesAsync();
    }

    private async Task CheckFirstResponseAsync(TicketSla sla, List<int> targetUserIds, DateTime nowUtc)
    {
        if (sla.FirstResponseMetAt.HasValue || !sla.FirstResponseDueAt.HasValue) return;

        var (warn, breach) = EvaluateMetric(sla.FirstResponseDueAt.Value, sla.FirstResponseWarned, sla.FirstResponseBreached, nowUtc, sla.CreatedAt);
        
        if (breach)
        {
            sla.FirstResponseBreached = true;
            await _notificationDispatcher.DispatchEventAsync("sla.breached", sla.TicketId, null, "First response SLA breached.");
            await TryEscalateAsync(sla);
        }
        else if (warn)
        {
            sla.FirstResponseWarned = true;
            foreach (var targetUserId in targetUserIds) {
                await CreateNotificationAsync(targetUserId, sla.TicketId, "SLA Warning", "First response SLA is about to be breached.");
            }
        }
    }

    private async Task CheckResolutionAsync(TicketSla sla, List<int> targetUserIds, DateTime nowUtc)
    {
        if (sla.ResolutionMetAt.HasValue || !sla.ResolutionDueAt.HasValue) return;

        var (warn, breach) = EvaluateMetric(sla.ResolutionDueAt.Value, sla.ResolutionWarned, sla.ResolutionBreached, nowUtc, sla.CreatedAt);
        
        if (breach)
        {
            sla.ResolutionBreached = true;
            await _notificationDispatcher.DispatchEventAsync("sla.breached", sla.TicketId, null, "Resolution SLA breached.");
            await TryEscalateAsync(sla);
        }
        else if (warn)
        {
            sla.ResolutionWarned = true;
            foreach (var targetUserId in targetUserIds) {
                await CreateNotificationAsync(targetUserId, sla.TicketId, "SLA Warning", "Resolution SLA is about to be breached.");
            }
        }
    }

    private async Task TryEscalateAsync(TicketSla sla)
    {
        if (sla.EscalatedAt.HasValue || sla.Ticket == null) return; // Idempotent check

        var policy = await _context.SlaPolicies
            .Where(p => p.IsActive && !p.IsDeleted && (p.ProjectId == sla.Ticket.ProjectId || p.ProjectId == null))
            .OrderByDescending(p => p.ProjectId.HasValue)
            .FirstOrDefaultAsync();

        if (policy == null || !policy.EscalateOnBreach) return;

        var target = await _context.SlaTargets
            .Where(t => t.IsActive && !t.IsDeleted && t.SlaPolicyId == policy.Id && t.PriorityId == sla.Ticket.PriorityId && 
                        (t.TicketTypeId == sla.Ticket.TypeId || t.TicketTypeId == null))
            .OrderByDescending(t => t.TicketTypeId.HasValue)
            .FirstOrDefaultAsync();

        if (target == null) return;

        sla.EscalatedAt = DateTime.UtcNow;
        
        // Priority Bump
        var higherPriority = await _context.Priorities
            .Where(p => p.SeverityLevel > sla.Ticket.Priority!.SeverityLevel)
            .OrderBy(p => p.SeverityLevel)
            .FirstOrDefaultAsync();

        if (higherPriority != null)
        {
            var oldPriority = sla.Ticket.PriorityId.ToString();
            sla.Ticket.PriorityId = higherPriority.Id;

            _context.TicketHistories.Add(new Domain.Entities.Ticket.TicketHistory
            {
                TicketId = sla.TicketId,
                Action = "Escalated",
                FieldName = "PriorityId",
                OldValue = oldPriority,
                NewValue = higherPriority.Id.ToString(),
                CreatedBy = "System"
            });
        }
    }

    public static (bool warn, bool breach) EvaluateMetric(DateTime dueAt, bool warned, bool breached, DateTime now, DateTime? createdAt = null)
    {
        var timeRemaining = (dueAt - now).TotalMinutes;
        
        if (timeRemaining <= 0 && !breached)
            return (false, true);
            
        if (timeRemaining > 0 && !warned)
        {
            double totalSpan = createdAt.HasValue && dueAt > createdAt.Value
                ? (dueAt - createdAt.Value).TotalMinutes
                : 120.0;

            // Warning threshold: 25% of total span, clamped between 5 and 60 minutes
            // If totalSpan is short, threshold is at most 50% of totalSpan
            double threshold = Math.Min(totalSpan * 0.5, Math.Max(5.0, Math.Min(60.0, totalSpan * 0.25)));

            if (timeRemaining <= threshold)
                return (true, false);
        }
            
        return (false, false);
    }

    
    private async Task CreateNotificationAsync(int userId, int ticketId, string title, string message)
    {
        var ticket = await _context.Tickets.FindAsync(ticketId);
        string ticketInfo = ticket != null ? $"[{ticket.TicketNumber}] {ticket.Title}" : $"Ticket #{ticketId}";
        string fullMessage = $"{message} ({ticketInfo})";

        _context.Notifications.Add(new Domain.Entities.Notification.Notification
        {
            UserId = userId,
            Title = title,
            Body = fullMessage,
            Type = "sla.warning",
            EntityId = ticketId,
            EntityType = "Ticket"
        });

        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            // The ticket title comes from the requester, so it is encoded before it goes into the email
            string titleHtml = WebUtility.HtmlEncode(title);
            string messageHtml = WebUtility.HtmlEncode(message);
            string ticketInfoHtml = WebUtility.HtmlEncode(ticketInfo);
            // Same setting as the other notification e-mails (NotificationDispatcher)
            string ticketUrl = WebUtility.HtmlEncode($"{(_config?["AppBaseUrl"] ?? string.Empty).TrimEnd('/')}/ticket-detail.html?id={ticketId}");
            string htmlBody = $@"
<div style=""font-family: Arial, sans-serif; padding: 20px; background-color: #f4f4f4; color: #333;"">
    <div style=""max-width: 600px; margin: 0 auto; background: #fff; border-radius: 8px; padding: 20px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);"">
        <div style=""border-bottom: 2px solid #ff4d4f; padding-bottom: 10px; margin-bottom: 20px;"">
            <h2 style=""color: #ff4d4f; margin: 0;"">⚠️ {titleHtml}</h2>
        </div>
        <div style=""font-size: 16px; line-height: 1.5;"">
            <p><strong>Warning:</strong> {messageHtml}</p>
            <p><strong>Ticket:</strong> <a href=""{ticketUrl}"" style=""color: #1890ff; text-decoration: none;"">{ticketInfoHtml}</a></p>
        </div>
        <div style=""margin-top: 30px; font-size: 12px; color: #999; text-align: center; border-top: 1px solid #eee; padding-top: 15px;"">
            This e-mail was sent automatically by the FNRC IT Service Desk.
        </div>
    </div>
</div>";

            await _emailService.SendEmailAsync(user.Email, $"{title} - {ticketInfo}", htmlBody, true);
        }
    }

}
