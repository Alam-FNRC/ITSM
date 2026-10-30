using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Notification;
using ItsTool.Domain.Entities.Security;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// Security incidents (feature spec, "Security incidents"; ISO 27001 A.5.24-A.5.28, A.6.8). Holders of
/// security.incident.view see every incident, holders of security.incident.manage also work on them, and people
/// added to one incident see and work on that one only. Everyone else gets "not found", as if it did not exist.
/// </summary>
public class SecurityIncidentService : ISecurityIncidentService
{
    private const int ReferenceAttempts = 3;
    private readonly ItsToolDbContext _context;
    private readonly IPermissionCalculator _permissions;
    private readonly ISignalRPusher? _pusher;

    public SecurityIncidentService(ItsToolDbContext context, IPermissionCalculator permissions, ISignalRPusher? pusher = null)
    {
        _context = context;
        _permissions = permissions;
        _pusher = pusher;
    }

    private sealed record Access(bool ViewAll, bool Manage, bool Member);

    private async Task<(bool ViewAll, bool Manage)> GetRightsAsync(int userId)
    {
        var perms = await _permissions.CalculateEffectivePermissionsAsync(userId);
        var manage = perms.Contains(PermissionConstants.SecurityIncidentManage);
        return (manage || perms.Contains(PermissionConstants.SecurityIncidentView), manage);
    }

    /// <summary>Loads an incident the caller may see, or throws "not found"; with requireManage, members pass only when memberMayAct.</summary>
    private async Task<(SecurityIncident Incident, Access Access)> LoadAsync(int userId, int incidentId, bool requireManage, bool memberMayAct = false)
    {
        var incident = await _context.SecurityIncidents.FirstOrDefaultAsync(i => i.Id == incidentId && !i.IsDeleted)
            ?? throw new KeyNotFoundException();
        var (viewAll, manage) = await GetRightsAsync(userId);
        var member = await _context.SecurityIncidentMembers.AnyAsync(m => m.IncidentId == incidentId && m.UserId == userId && !m.IsDeleted);
        if (!viewAll && !member) throw new KeyNotFoundException();
        if (requireManage && !manage && !(memberMayAct && member)) throw new UnauthorizedAccessException();
        return (incident, new Access(viewAll, manage, member));
    }

    private void Record(SecurityIncident incident, int userId, string kind, string? field = null, string? oldValue = null, string? newValue = null, string? note = null) =>
        incident.Actions.Add(new SecurityIncidentAction { IncidentId = incident.Id, UserId = userId, Kind = kind, Field = field, OldValue = oldValue, NewValue = newValue, Note = note });

    // ---- Reporting (any signed-in user, A.6.8) ----

    public async Task<MySecurityReportDto> ReportAsync(int userId, ReportSecurityIssueDto dto)
    {
        var incident = new SecurityIncident
        {
            Title = Required(dto.Title, "Title"),
            Description = Required(dto.Description, "Description"),
            OccurredAt = dto.OccurredAt,
            Source = SecurityIncidentSource.StaffReport,
            ReporterUserId = userId
        };
        Record(incident, userId, SecurityIncidentActions.Reported);
        await SaveNewAsync(incident);

        await NotifyAsync(await UsersWithPermissionAsync(PermissionConstants.SecurityIncidentManage), incident,
            "security.reported", "New security report", $"Security report {incident.Reference} was received.");
        return ToMyReport(incident);
    }

    public async Task<IReadOnlyList<MySecurityReportDto>> GetMyReportsAsync(int userId)
    {
        var mine = await _context.SecurityIncidents.AsNoTracking()
            .Where(i => i.ReporterUserId == userId && i.Source == SecurityIncidentSource.StaffReport && !i.IsDeleted)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
        return mine.Select(ToMyReport).ToList();
    }

    // The reporter sees progress only, not the security team's phases or findings
    private static MySecurityReportDto ToMyReport(SecurityIncident i) => new(i.Reference, i.Title, i.CreatedAt,
        i.Status switch { SecurityIncidentStatus.New => "Received", SecurityIncidentStatus.Closed => "Closed", _ => "InProgress" });

    // ---- Security team ----

    public async Task<IReadOnlyList<SecurityIncidentListItemDto>> ListAsync(int userId, SecurityIncidentFilterDto filter)
    {
        var (viewAll, _) = await GetRightsAsync(userId);
        var query = _context.SecurityIncidents.AsNoTracking().Where(i => !i.IsDeleted);
        if (!viewAll)
        {
            query = query.Where(i => _context.SecurityIncidentMembers.Any(m => m.IncidentId == i.Id && m.UserId == userId && !m.IsDeleted));
        }
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = ParseEnum<SecurityIncidentStatus>(filter.Status, "Status");
            query = query.Where(i => i.Status == status);
        }
        if (filter.OpenOnly) query = query.Where(i => i.Status != SecurityIncidentStatus.Closed);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(i => i.Reference.Contains(search) || i.Title.Contains(search));
        }

        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                Incident = i,
                Items = i.AffectedItems.Count(a => !a.IsDeleted),
                OpenItems = i.AffectedItems.Count(a => !a.IsDeleted && a.Status == SecurityAffectedItemStatus.Open)
            })
            .ToListAsync();
        var names = await UserNamesAsync(rows.Where(r => r.Incident.HandlerUserId.HasValue).Select(r => r.Incident.HandlerUserId!.Value));

        return rows.Select(r => new SecurityIncidentListItemDto(
            r.Incident.Id, r.Incident.Reference, r.Incident.Title, r.Incident.Source.ToString(), r.Incident.Status.ToString(),
            r.Incident.TriageOutcome.ToString(), r.Incident.Category.ToString(), r.Incident.Severity, r.Incident.PersonalDataInvolved,
            r.Incident.HandlerUserId, r.Incident.HandlerUserId.HasValue ? names.GetValueOrDefault(r.Incident.HandlerUserId.Value) : null,
            r.Incident.CreatedAt, r.Items, r.OpenItems)).ToList();
    }

    public async Task<SecurityIncidentDetailDto> CreateAsync(int userId, CreateSecurityIncidentDto dto)
    {
        var (_, manage) = await GetRightsAsync(userId);
        if (!manage) throw new UnauthorizedAccessException();

        var incident = new SecurityIncident
        {
            Title = Required(dto.Title, "Title"),
            Description = Required(dto.Description, "Description"),
            OccurredAt = dto.OccurredAt,
            Source = SecurityIncidentSource.SecurityTeam,
            ReporterUserId = userId,
            HandlerUserId = userId,
            Status = SecurityIncidentStatus.Triage,
            Category = string.IsNullOrWhiteSpace(dto.Category) ? SecurityIncidentCategory.Unclassified : ParseEnum<SecurityIncidentCategory>(dto.Category, "Category"),
            Severity = ValidSeverity(dto.Severity),
            PersonalDataInvolved = dto.PersonalDataInvolved
        };
        Record(incident, userId, SecurityIncidentActions.Reported);
        await SaveNewAsync(incident);
        return await GetAsync(userId, incident.Id);
    }

    public async Task<SecurityIncidentDetailDto> GetAsync(int userId, int incidentId)
    {
        var (incident, access) = await LoadAsync(userId, incidentId, requireManage: false);

        // A.5.24: every opening is recorded, by the security team as much as anyone
        _context.SecurityIncidentAccesses.Add(new SecurityIncidentAccess { IncidentId = incidentId, UserId = userId });
        await _context.SaveChangesAsync();

        var actions = await _context.SecurityIncidentActions.AsNoTracking()
            .Where(a => a.IncidentId == incidentId).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync();
        var items = await _context.SecurityAffectedItems.AsNoTracking()
            .Where(a => a.IncidentId == incidentId && !a.IsDeleted).OrderBy(a => a.Id).ToListAsync();
        var members = await _context.SecurityIncidentMembers.AsNoTracking()
            .Where(m => m.IncidentId == incidentId && !m.IsDeleted).OrderBy(m => m.CreatedAt).ToListAsync();

        var userIds = new List<int> { incident.ReporterUserId };
        userIds.AddRange(new[] { incident.HandlerUserId, incident.TriagedByUserId, incident.ReviewCompletedByUserId }.Where(id => id.HasValue).Select(id => id!.Value));
        userIds.AddRange(actions.Select(a => a.UserId));
        userIds.AddRange(members.SelectMany(m => new[] { m.UserId, m.AddedByUserId }));
        userIds.AddRange(items.Where(a => a.StatusChangedByUserId.HasValue).Select(a => a.StatusChangedByUserId!.Value));
        var names = await UserNamesAsync(userIds);

        return new SecurityIncidentDetailDto(
            incident.Id, incident.Reference, incident.Title, incident.Description, incident.Source.ToString(), incident.Status.ToString(),
            incident.ReporterUserId, incident.CreatedAt, incident.OccurredAt, incident.HandlerUserId,
            incident.TriageOutcome.ToString(), incident.TriageReason, incident.TriagedAt, incident.TriagedByUserId,
            incident.Category.ToString(), incident.Severity, incident.PersonalDataInvolved,
            incident.ContainedAt, incident.EradicatedAt, incident.RecoveredAt, incident.ClosedAt,
            incident.ReviewRootCause, incident.ReviewWhatWorked, incident.ReviewImprovements, incident.ReviewFollowUps, incident.ReviewCompletedAt,
            ReviewRequired(incident),
            actions.Select(a => new SecurityIncidentActionDto(a.Id, a.UserId, a.Kind, a.Field, a.OldValue, a.NewValue, a.Note, a.CreatedAt)).ToList(),
            items.Select(a => new SecurityAffectedItemDto(a.Id, a.Kind.ToString(), a.Name, a.Status.ToString(), a.StatusReason, a.StatusChangedAt, a.StatusChangedByUserId)).ToList(),
            members.Select(m => new SecurityIncidentMemberDto(m.UserId, m.AddedByUserId, m.CreatedAt)).ToList(),
            names,
            access.Manage,
            access.ViewAll);
    }

    public async Task UpdateAsync(int userId, int incidentId, UpdateSecurityIncidentDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        var category = ParseEnum<SecurityIncidentCategory>(dto.Category, "Category");
        var severity = ValidSeverity(dto.Severity);
        if (dto.HandlerUserId.HasValue && !await _context.Users.AnyAsync(u => u.Id == dto.HandlerUserId && u.IsActive && !u.IsDeleted))
            throw new ArgumentException("The handler must be an active user.");

        void Change(string field, string? oldValue, string? newValue)
        {
            if (oldValue != newValue) Record(incident, userId, SecurityIncidentActions.Updated, field, oldValue, newValue);
        }
        Change("Title", incident.Title, Required(dto.Title, "Title"));
        Change("Description", incident.Description, Required(dto.Description, "Description"));
        Change("OccurredAt", incident.OccurredAt?.ToString("O"), dto.OccurredAt?.ToString("O"));
        Change("Category", incident.Category.ToString(), category.ToString());
        Change("Severity", incident.Severity?.ToString(), severity?.ToString());
        Change("PersonalDataInvolved", incident.PersonalDataInvolved.ToString(), dto.PersonalDataInvolved.ToString());
        Change("HandlerUserId", incident.HandlerUserId?.ToString(), dto.HandlerUserId?.ToString());

        incident.Title = dto.Title.Trim();
        incident.Description = dto.Description.Trim();
        incident.OccurredAt = dto.OccurredAt;
        incident.Category = category;
        incident.Severity = severity;
        incident.PersonalDataInvolved = dto.PersonalDataInvolved;
        incident.HandlerUserId = dto.HandlerUserId;
        await _context.SaveChangesAsync();
    }

    /// <summary>A.5.25: the security team decides what a report is, with a reason.</summary>
    public async Task TriageAsync(int userId, int incidentId, TriageSecurityIncidentDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        var outcome = ParseEnum<SecurityTriageOutcome>(dto.Outcome, "Outcome");
        if (outcome == SecurityTriageOutcome.Pending) throw new ArgumentException("Choose what the report is.");
        var reason = Required(dto.Reason, "Reason");

        if (outcome == SecurityTriageOutcome.Incident)
        {
            var category = string.IsNullOrWhiteSpace(dto.Category) ? incident.Category : ParseEnum<SecurityIncidentCategory>(dto.Category, "Category");
            var severity = ValidSeverity(dto.Severity) ?? incident.Severity;
            if (category == SecurityIncidentCategory.Unclassified || severity == null)
                throw new ArgumentException("An incident needs a category and a severity.");
            if (category != incident.Category) Record(incident, userId, SecurityIncidentActions.Updated, "Category", incident.Category.ToString(), category.ToString());
            if (severity != incident.Severity) Record(incident, userId, SecurityIncidentActions.Updated, "Severity", incident.Severity?.ToString(), severity.ToString());
            incident.Category = category;
            incident.Severity = severity;
        }

        Record(incident, userId, SecurityIncidentActions.Triaged, "TriageOutcome", incident.TriageOutcome.ToString(), outcome.ToString(), reason);
        incident.TriageOutcome = outcome;
        incident.TriageReason = reason;
        incident.TriagedAt = DateTime.UtcNow;
        incident.TriagedByUserId = userId;
        incident.HandlerUserId ??= userId;

        // Not a security issue, or an event that needs no response: the case is closed. An incident goes to containment.
        var next = outcome == SecurityTriageOutcome.Incident ? SecurityIncidentStatus.Containment : SecurityIncidentStatus.Closed;
        if (outcome != SecurityTriageOutcome.Incident || incident.Status is SecurityIncidentStatus.New or SecurityIncidentStatus.Triage or SecurityIncidentStatus.Closed)
        {
            MoveTo(incident, userId, next, null);
        }
        await _context.SaveChangesAsync();
    }

    public async Task ChangeStatusAsync(int userId, int incidentId, ChangeSecurityStatusDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        var target = ParseEnum<SecurityIncidentStatus>(dto.Status, "Status");
        if (target == incident.Status) return;
        var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

        if (target == SecurityIncidentStatus.New)
            throw new ArgumentException("An incident cannot go back to New.");
        if (target is >= SecurityIncidentStatus.Containment and <= SecurityIncidentStatus.Review && incident.TriageOutcome != SecurityTriageOutcome.Incident)
            throw new ArgumentException("Only a report triaged as an incident goes through the response phases.");
        if (target == SecurityIncidentStatus.Closed)
        {
            if (incident.TriageOutcome == SecurityTriageOutcome.Pending)
                throw new ArgumentException("Triage the report before closing it.");
            if (ReviewRequired(incident) && incident.ReviewCompletedAt == null)
                throw new ArgumentException("A severity 1 or 2 incident needs a post-incident review before it is closed.");
        }
        // Going back, or reopening, must say why
        if ((target < incident.Status || incident.Status == SecurityIncidentStatus.Closed) && note == null)
            throw new ArgumentException("Give a reason for moving the incident back.");

        if (target == SecurityIncidentStatus.Triage) incident.HandlerUserId ??= userId;
        MoveTo(incident, userId, target, note);
        await _context.SaveChangesAsync();
    }

    private void MoveTo(SecurityIncident incident, int userId, SecurityIncidentStatus target, string? note)
    {
        if (target == incident.Status) return;
        Record(incident, userId, SecurityIncidentActions.StatusChanged, "Status", incident.Status.ToString(), target.ToString(), note);
        var now = DateTime.UtcNow;
        switch (target)
        {
            case SecurityIncidentStatus.Containment: incident.ContainedAt ??= now; break;
            case SecurityIncidentStatus.Eradication: incident.EradicatedAt ??= now; break;
            case SecurityIncidentStatus.Recovery: incident.RecoveredAt ??= now; break;
            case SecurityIncidentStatus.Closed: incident.ClosedAt = now; break;
        }
        if (incident.Status == SecurityIncidentStatus.Closed) incident.ClosedAt = null;
        incident.Status = target;
    }

    public async Task AddNoteAsync(int userId, int incidentId, AddSecurityNoteDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true, memberMayAct: true);
        Record(incident, userId, SecurityIncidentActions.Note, note: Required(dto.Text, "Text"));
        await _context.SaveChangesAsync();
    }

    public async Task AddMemberAsync(int userId, int incidentId, AddSecurityMemberDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        if (!await _context.Users.AnyAsync(u => u.Id == dto.UserId && u.IsActive && !u.IsDeleted))
            throw new ArgumentException("Choose an active user.");

        var existing = await _context.SecurityIncidentMembers.FirstOrDefaultAsync(m => m.IncidentId == incidentId && m.UserId == dto.UserId);
        if (existing is { IsDeleted: false }) return;
        if (existing != null)
        {
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.AddedByUserId = userId;
        }
        else
        {
            _context.SecurityIncidentMembers.Add(new SecurityIncidentMember { IncidentId = incidentId, UserId = dto.UserId, AddedByUserId = userId });
        }
        Record(incident, userId, SecurityIncidentActions.MemberAdded, "UserId", null, dto.UserId.ToString());
        await _context.SaveChangesAsync();

        await NotifyAsync(new[] { dto.UserId }, incident, "security.member.added", "Added to a security incident",
            $"You were added to security incident {incident.Reference}.");
    }

    public async Task RemoveMemberAsync(int userId, int incidentId, int memberUserId)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        var member = await _context.SecurityIncidentMembers.FirstOrDefaultAsync(m => m.IncidentId == incidentId && m.UserId == memberUserId && !m.IsDeleted)
            ?? throw new ArgumentException("That user is not on this incident.");
        member.IsDeleted = true;
        member.DeletedAt = DateTime.UtcNow;
        Record(incident, userId, SecurityIncidentActions.MemberRemoved, "UserId", memberUserId.ToString(), null);
        await _context.SaveChangesAsync();
    }

    public async Task AddAffectedItemsAsync(int userId, int incidentId, AddSecurityAffectedItemsDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        foreach (var item in dto.Items)
        {
            var kind = ParseEnum<SecurityAffectedItemKind>(item.Kind, "Kind");
            var name = Required(item.Name, "Name");
            incident.AffectedItems.Add(new SecurityAffectedItem { IncidentId = incidentId, Kind = kind, Name = name });
            Record(incident, userId, SecurityIncidentActions.AffectedItemAdded, "Kind", null, kind.ToString(), name);
        }
        await _context.SaveChangesAsync();
    }

    /// <summary>The security team, or someone added to the incident (e.g. the admin who patched the server), ticks an item off.</summary>
    public async Task UpdateAffectedItemAsync(int userId, int incidentId, int itemId, UpdateSecurityAffectedItemDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true, memberMayAct: true);
        var item = await _context.SecurityAffectedItems.FirstOrDefaultAsync(a => a.Id == itemId && a.IncidentId == incidentId && !a.IsDeleted)
            ?? throw new KeyNotFoundException();
        var status = ParseEnum<SecurityAffectedItemStatus>(dto.Status, "Status");
        var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
        if (status is SecurityAffectedItemStatus.FalsePositive or SecurityAffectedItemStatus.AcceptedRisk && reason == null)
            throw new ArgumentException("Give the reason for a false positive or an accepted risk.");
        if (status == item.Status && reason == item.StatusReason) return;

        Record(incident, userId, SecurityIncidentActions.AffectedItemUpdated, "Status", item.Status.ToString(), status.ToString(),
            reason == null ? item.Name : $"{item.Name}: {reason}");
        item.Status = status;
        item.StatusReason = reason;
        item.StatusChangedAt = DateTime.UtcNow;
        item.StatusChangedByUserId = userId;
        await _context.SaveChangesAsync();
    }

    /// <summary>A.5.27: the review counts as done once it names the root cause and what to improve.</summary>
    public async Task SaveReviewAsync(int userId, int incidentId, SaveSecurityReviewDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        incident.ReviewRootCause = Trimmed(dto.RootCause);
        incident.ReviewWhatWorked = Trimmed(dto.WhatWorked);
        incident.ReviewImprovements = Trimmed(dto.Improvements);
        incident.ReviewFollowUps = Trimmed(dto.FollowUps);

        var complete = incident.ReviewRootCause != null && incident.ReviewImprovements != null;
        if (complete)
        {
            incident.ReviewCompletedAt = DateTime.UtcNow;
            incident.ReviewCompletedByUserId = userId;
        }
        else
        {
            incident.ReviewCompletedAt = null;
            incident.ReviewCompletedByUserId = null;
        }
        Record(incident, userId, SecurityIncidentActions.ReviewSaved, "ReviewComplete", null, complete.ToString());
        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<SecurityIncidentAccessDto>> GetAccessLogAsync(int userId, int incidentId)
    {
        var (_, access) = await LoadAsync(userId, incidentId, requireManage: false);
        if (!access.ViewAll) throw new UnauthorizedAccessException();

        var rows = await _context.SecurityIncidentAccesses.AsNoTracking()
            .Where(a => a.IncidentId == incidentId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        var names = await UserNamesAsync(rows.Select(r => r.UserId));
        return rows.Select(r => new SecurityIncidentAccessDto(r.UserId, names.GetValueOrDefault(r.UserId), r.CreatedAt)).ToList();
    }

    // ---- Helpers ----

    private static bool ReviewRequired(SecurityIncident incident) =>
        incident.TriageOutcome == SecurityTriageOutcome.Incident && incident.Severity is 1 or 2;

    /// <summary>References run per year (SEC-2026-0001); two reports saved at once take the next number on retry.</summary>
    private async Task SaveNewAsync(SecurityIncident incident)
    {
        for (var attempt = 1; ; attempt++)
        {
            var prefix = $"SEC-{DateTime.UtcNow.Year}-";
            var count = await _context.SecurityIncidents.CountAsync(i => i.Reference.StartsWith(prefix));
            incident.Reference = $"{prefix}{count + attempt:D4}";
            if (incident.Id == 0) _context.SecurityIncidents.Add(incident);
            try
            {
                await _context.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < ReferenceAttempts)
            {
                // The number was taken in the meantime; try the next one
            }
        }
    }

    /// <summary>Users holding a permission through a role, a group's role or a grant, minus denies; mirrors PermissionCalculator.</summary>
    private async Task<List<int>> UsersWithPermissionAsync(string key)
    {
        var permissionId = await _context.Permissions.Where(p => p.Key == key && p.IsActive && !p.IsDeleted).Select(p => (int?)p.Id).FirstOrDefaultAsync();
        if (permissionId == null) return new List<int>();

        var roleIds = await _context.RolePermissions
            .Where(rp => rp.PermissionId == permissionId && !rp.IsDeleted && rp.Role != null && rp.Role.IsActive && !rp.Role.IsDeleted)
            .Select(rp => rp.RoleId).ToListAsync();
        var viaRoles = await _context.UserRoles.Where(ur => roleIds.Contains(ur.RoleId) && !ur.IsDeleted).Select(ur => ur.UserId).ToListAsync();
        var groupIds = await _context.GroupRoles.Where(gr => roleIds.Contains(gr.RoleId) && !gr.IsDeleted).Select(gr => gr.GroupId).ToListAsync();
        var viaGroups = await _context.GroupMembers
            .Where(gm => groupIds.Contains(gm.GroupId) && !gm.IsDeleted && gm.Group != null && gm.Group.IsActive && !gm.Group.IsDeleted)
            .Select(gm => gm.UserId).ToListAsync();
        var overrides = await _context.UserPermissionOverrides.Where(o => o.PermissionId == permissionId && !o.IsDeleted)
            .Select(o => new { o.UserId, o.IsGranted }).ToListAsync();

        var holders = viaRoles.Concat(viaGroups).Concat(overrides.Where(o => o.IsGranted).Select(o => o.UserId)).ToHashSet();
        holders.ExceptWith(overrides.Where(o => !o.IsGranted).Select(o => o.UserId));
        return await _context.Users.Where(u => holders.Contains(u.Id) && u.IsActive && !u.IsDeleted).Select(u => u.Id).ToListAsync();
    }

    // Notifications name the reference only; the details stay behind the incident's access check
    private async Task NotifyAsync(IEnumerable<int> userIds, SecurityIncident incident, string type, string title, string body)
    {
        var recipients = userIds.Distinct().ToList();
        if (recipients.Count == 0) return;
        foreach (var recipient in recipients)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = recipient, Type = type, Title = title, Body = body,
                EntityType = "SecurityIncident", EntityId = incident.Id, Priority = "High"
            });
        }
        await _context.SaveChangesAsync();
        if (_pusher == null) return;
        foreach (var recipient in recipients)
        {
            await _pusher.PushNotificationAsync(recipient, new
            {
                type, title, body, entityType = "SecurityIncident", entityId = incident.Id, priority = "High", createdAt = DateTime.UtcNow
            });
        }
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids)
    {
        var wanted = ids.Distinct().ToList();
        return await _context.Users.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim());
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field} is required.") : value.Trim();

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ValidSeverity(int? severity) =>
        severity is null or (>= 1 and <= 4) ? severity : throw new ArgumentException("Severity runs from 1 (critical) to 4 (low).");

    // Names only: Enum.TryParse would also take numbers, including ones no member has
    internal static T ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || char.IsDigit(text[0]) || text[0] == '-' ||
            !Enum.TryParse<T>(text, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException($"{field} '{value}' is not valid.");
        }
        return parsed;
    }
}

/// <summary>Kinds of entry in a security incident's history.</summary>
public static class SecurityIncidentActions
{
    public const string Reported = "Reported";
    public const string Updated = "Updated";
    public const string Triaged = "Triaged";
    public const string StatusChanged = "StatusChanged";
    public const string Note = "Note";
    public const string MemberAdded = "MemberAdded";
    public const string MemberRemoved = "MemberRemoved";
    public const string AffectedItemAdded = "AffectedItemAdded";
    public const string AffectedItemUpdated = "AffectedItemUpdated";
    public const string ReviewSaved = "ReviewSaved";
}
