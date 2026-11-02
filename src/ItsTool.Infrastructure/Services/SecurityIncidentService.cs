using System.Security.Cryptography;
using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Notification;
using ItsTool.Domain.Entities.Security;
using ItsTool.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
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
    private readonly IEvidenceStore _evidenceStore;
    private readonly ISignalRPusher? _pusher;

    public SecurityIncidentService(ItsToolDbContext context, IPermissionCalculator permissions, IEvidenceStore evidenceStore, ISignalRPusher? pusher = null)
    {
        _context = context;
        _permissions = permissions;
        _evidenceStore = evidenceStore;
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
        var evidence = await _context.SecurityEvidence.AsNoTracking().Include(e => e.Custody)
            .Where(e => e.IncidentId == incidentId).OrderBy(e => e.Number).ToListAsync();

        var userIds = new List<int> { incident.ReporterUserId };
        userIds.AddRange(new[] { incident.HandlerUserId, incident.TriagedByUserId, incident.ReviewCompletedByUserId }.Where(id => id.HasValue).Select(id => id!.Value));
        userIds.AddRange(actions.Select(a => a.UserId));
        userIds.AddRange(members.SelectMany(m => new[] { m.UserId, m.AddedByUserId }));
        userIds.AddRange(items.Where(a => a.StatusChangedByUserId.HasValue).Select(a => a.StatusChangedByUserId!.Value));
        userIds.AddRange(evidence.SelectMany(e => new[] { e.CollectedByUserId, e.RecordedByUserId }));
        userIds.AddRange(evidence.SelectMany(e => e.Custody).SelectMany(c => new[] { (int?)c.UserId, c.CustodianUserId }).Where(id => id.HasValue).Select(id => id!.Value));
        var names = await UserNamesAsync(userIds);

        return new SecurityIncidentDetailDto(
            incident.Id, incident.Reference, incident.Title, incident.Description, incident.Source.ToString(), incident.Status.ToString(),
            incident.ReporterUserId, incident.CreatedAt, incident.OccurredAt, incident.HandlerUserId,
            incident.TriageOutcome.ToString(), incident.TriageReason, incident.TriagedAt, incident.TriagedByUserId,
            incident.Category.ToString(), incident.Severity, incident.PersonalDataInvolved,
            incident.ClosedAt,
            incident.ReviewRootCause, incident.ReviewWhatWorked, incident.ReviewImprovements, incident.ReviewFollowUps, incident.ReviewCompletedAt,
            ReviewRequired(incident),
            actions.Select(a => new SecurityIncidentActionDto(a.Id, a.UserId, a.Kind, a.Field, a.OldValue, a.NewValue, a.Note, a.CreatedAt)).ToList(),
            items.Select(a => new SecurityAffectedItemDto(a.Id, a.Kind.ToString(), a.Name, a.Status.ToString(), a.StatusReason, a.StatusChangedAt, a.StatusChangedByUserId)).ToList(),
            members.Select(m => new SecurityIncidentMemberDto(m.UserId, m.AddedByUserId, m.CreatedAt)).ToList(),
            evidence.Select(ToEvidenceDto).ToList(),
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

        // Not a security issue, or an event that needs no response: the case is closed. An incident is responded to.
        var next = outcome == SecurityTriageOutcome.Incident ? SecurityIncidentStatus.Responding : SecurityIncidentStatus.Closed;
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
        if (target == SecurityIncidentStatus.Responding && incident.TriageOutcome != SecurityTriageOutcome.Incident)
            throw new ArgumentException("Only a report triaged as an incident is responded to.");
        if (target == SecurityIncidentStatus.Closed)
        {
            if (incident.TriageOutcome == SecurityTriageOutcome.Pending)
                throw new ArgumentException("Triage the report before closing it.");
            if (ReviewRequired(incident) && incident.ReviewCompletedAt == null)
                throw new ArgumentException("A severity 1 or 2 incident needs its lessons learned before it is closed.");
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
        // Closing stamps the time; reopening clears it
        incident.ClosedAt = target == SecurityIncidentStatus.Closed ? DateTime.UtcNow : null;
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

    /// <summary>A.5.27: the lessons learned (what to improve) are the one required part; the other fields are optional.</summary>
    public async Task SaveReviewAsync(int userId, int incidentId, SaveSecurityReviewDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        incident.ReviewRootCause = Trimmed(dto.RootCause);
        incident.ReviewWhatWorked = Trimmed(dto.WhatWorked);
        incident.ReviewImprovements = Trimmed(dto.Improvements);
        incident.ReviewFollowUps = Trimmed(dto.FollowUps);

        var complete = incident.ReviewImprovements != null;
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

    // ---- Evidence (A.5.28) ----

    /// <summary>
    /// The security team, or someone on the incident, records evidence: a file kept here, fingerprinted as it is
    /// written, or an item kept elsewhere with who holds it and where.
    /// </summary>
    public async Task<SecurityEvidenceDto> AddEvidenceAsync(int userId, int incidentId, AddSecurityEvidenceDto dto, IFormFile? file)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true, memberMayAct: true);
        var kind = ParseEnum<SecurityEvidenceKind>(dto.Kind, "Kind");
        var description = Required(dto.Description, "Description");
        var location = Trimmed(dto.Location);
        var collectedBy = dto.CollectedByUserId ?? userId;
        if (collectedBy != userId && !await _context.Users.AnyAsync(u => u.Id == collectedBy && !u.IsDeleted))
            throw new ArgumentException("Choose who collected the evidence.");
        var collectedAt = dto.CollectedAt?.ToUniversalTime() ?? DateTime.UtcNow;
        if (collectedAt > DateTime.UtcNow.AddMinutes(5))
            throw new ArgumentException("Evidence cannot be collected in the future.");
        if (file == null && location == null)
            throw new ArgumentException("Attach the file, or say where the evidence is kept.");

        string? fileName = null;
        StoredEvidenceFile? stored = null;
        if (file != null)
        {
            fileName = UploadPolicy.DisplayName(file.FileName);
            var extension = Path.GetExtension(fileName);
            if (file.Length == 0) throw new ArgumentException("The file is empty.");
            if (file.Length > EvidencePolicy.MaxBytes) throw new ArgumentException("Evidence files can be up to 25 MB. Compress or split a larger one.");
            if (fileName.Length is 0 or > 255) throw new ArgumentException("The file name must be 1 to 255 characters.");
            if (!EvidencePolicy.IsAllowed(fileName)) throw new ArgumentException($"Files of type '{extension}' cannot be kept as evidence.");

            await using var content = file.OpenReadStream();
            var header = new byte[UploadPolicy.HeaderLength];
            var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
            if (!EvidencePolicy.HasExpectedSignature(fileName, header.AsSpan(0, read)))
                throw new ArgumentException($"The file content is not a valid '{extension}' file.");
            content.Position = 0;
            stored = await _evidenceStore.SaveAsync(incidentId, content, extension);
        }

        var evidence = new SecurityEvidence
        {
            IncidentId = incidentId,
            Kind = kind,
            Description = description,
            CollectedByUserId = collectedBy,
            CollectedAt = collectedAt,
            RecordedByUserId = userId,
            FileName = fileName,
            FilePath = stored?.Path,
            FileSize = stored?.Size,
            Sha256 = stored?.Sha256
        };
        // A file is held here; an item kept elsewhere is held by whoever collected it until it is handed over
        evidence.Custody.Add(new SecurityEvidenceCustody
        {
            Kind = SecurityCustodyKind.Collected, UserId = userId, CustodianUserId = stored == null ? collectedBy : null,
            Location = location, Sha256 = stored?.Sha256
        });
        _context.SecurityEvidence.Add(evidence);
        var action = new SecurityIncidentAction { IncidentId = incidentId, UserId = userId, Kind = SecurityIncidentActions.EvidenceAdded, Field = "Evidence" };
        incident.Actions.Add(action);

        // Numbers run per incident; two uploads saved at once take the next number on retry
        for (var attempt = 1; ; attempt++)
        {
            evidence.Number = (await _context.SecurityEvidence.Where(e => e.IncidentId == incidentId).MaxAsync(e => (int?)e.Number) ?? 0) + 1;
            action.NewValue = evidence.Number.ToString();
            try
            {
                await _context.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException) when (attempt < ReferenceAttempts)
            {
                // The number was taken in the meantime; try the next one
            }
        }
        return ToEvidenceDto(evidence);
    }

    /// <summary>An item kept elsewhere, such as a seized laptop, passes to someone else or moves; the security team records it.</summary>
    public async Task HandOverEvidenceAsync(int userId, int incidentId, int evidenceId, HandOverSecurityEvidenceDto dto)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: true);
        var evidence = await FindEvidenceAsync(incidentId, evidenceId);
        if (evidence.FilePath != null)
            throw new ArgumentException("A file kept here is not handed over; every download of it is recorded instead.");
        if (!await _context.Users.AnyAsync(u => u.Id == dto.ToUserId && u.IsActive && !u.IsDeleted))
            throw new ArgumentException("Choose an active user.");
        var location = Required(dto.Location, "Location");

        _context.SecurityEvidenceCustody.Add(new SecurityEvidenceCustody
        {
            EvidenceId = evidence.Id, Kind = SecurityCustodyKind.HandedOver, UserId = userId,
            CustodianUserId = dto.ToUserId, Location = location, Note = Trimmed(dto.Note)
        });
        Record(incident, userId, SecurityIncidentActions.EvidenceHandedOver, "Evidence", evidence.Number.ToString(), dto.ToUserId.ToString(), location);
        await _context.SaveChangesAsync();
    }

    /// <summary>Anyone who can see the incident may check that a file still matches the fingerprint taken on upload.</summary>
    public async Task<SecurityEvidenceCheckDto> CheckEvidenceAsync(int userId, int incidentId, int evidenceId)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: false);
        var evidence = await FindFileEvidenceAsync(incidentId, evidenceId);
        var (content, found) = await OpenAndHashAsync(evidence);
        content?.Dispose();

        var intact = found == evidence.Sha256;
        if (intact)
        {
            _context.SecurityEvidenceCustody.Add(new SecurityEvidenceCustody { EvidenceId = evidence.Id, Kind = SecurityCustodyKind.Verified, UserId = userId, Sha256 = found });
            await _context.SaveChangesAsync();
        }
        else
        {
            await RecordIntegrityFailureAsync(incident, evidence, userId, found);
        }
        return new SecurityEvidenceCheckDto(intact, evidence.Sha256!, found, DateTime.UtcNow);
    }

    /// <summary>
    /// A copy of an evidence file for anyone who can see the incident. The fingerprint is checked on the very
    /// stream that is sent, and a file that no longer matches is refused rather than passed on.
    /// </summary>
    public async Task<SecurityEvidenceFileDto> OpenEvidenceAsync(int userId, int incidentId, int evidenceId)
    {
        var (incident, _) = await LoadAsync(userId, incidentId, requireManage: false);
        var evidence = await FindFileEvidenceAsync(incidentId, evidenceId);
        var (content, found) = await OpenAndHashAsync(evidence);
        if (content == null || found != evidence.Sha256)
        {
            content?.Dispose();
            await RecordIntegrityFailureAsync(incident, evidence, userId, found);
            throw new InvalidDataException($"Evidence E{evidence.Number} is missing or no longer matches its fingerprint, so it was not sent. The security team has been told.");
        }

        try
        {
            _context.SecurityEvidenceCustody.Add(new SecurityEvidenceCustody { EvidenceId = evidence.Id, Kind = SecurityCustodyKind.Downloaded, UserId = userId, Sha256 = found });
            await _context.SaveChangesAsync();
        }
        catch
        {
            // No copy leaves without its record
            content.Dispose();
            throw;
        }
        return new SecurityEvidenceFileDto(content, evidence.FileName!);
    }

    private async Task<SecurityEvidence> FindEvidenceAsync(int incidentId, int evidenceId) =>
        await _context.SecurityEvidence.FirstOrDefaultAsync(e => e.Id == evidenceId && e.IncidentId == incidentId)
            ?? throw new KeyNotFoundException();

    private async Task<SecurityEvidence> FindFileEvidenceAsync(int incidentId, int evidenceId)
    {
        var evidence = await FindEvidenceAsync(incidentId, evidenceId);
        return evidence.FilePath != null ? evidence : throw new ArgumentException("This evidence has no file kept here.");
    }

    /// <summary>The stored file, rewound, with its SHA-256; both null when the file is missing.</summary>
    private async Task<(Stream? Content, string? Sha256)> OpenAndHashAsync(SecurityEvidence evidence)
    {
        var content = _evidenceStore.OpenRead(evidence.FilePath!);
        if (content == null) return (null, null);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(content)).ToLowerInvariant();
        content.Position = 0;
        return (content, hash);
    }

    private async Task RecordIntegrityFailureAsync(SecurityIncident incident, SecurityEvidence evidence, int userId, string? found)
    {
        _context.SecurityEvidenceCustody.Add(new SecurityEvidenceCustody { EvidenceId = evidence.Id, Kind = SecurityCustodyKind.IntegrityFailed, UserId = userId, Sha256 = found });
        Record(incident, userId, SecurityIncidentActions.EvidenceIntegrityFailed, "Evidence", null, evidence.Number.ToString());
        await _context.SaveChangesAsync();
        await NotifyAsync(await UsersWithPermissionAsync(PermissionConstants.SecurityIncidentManage), incident, "security.evidence.failed",
            "Evidence check failed", $"Evidence E{evidence.Number} on security incident {incident.Reference} failed its fingerprint check.");
    }

    private static SecurityEvidenceDto ToEvidenceDto(SecurityEvidence e)
    {
        var custody = e.Custody.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
        var holder = custody.LastOrDefault(c => c.Kind is SecurityCustodyKind.Collected or SecurityCustodyKind.HandedOver);
        return new SecurityEvidenceDto(
            e.Id, e.Number, e.Kind.ToString(), e.Description, e.CollectedByUserId, e.CollectedAt, e.RecordedByUserId, e.CreatedAt,
            e.FileName, e.FileSize, e.Sha256, holder?.CustodianUserId, holder?.Location,
            custody.Select(c => new SecurityEvidenceCustodyDto(c.Id, c.Kind.ToString(), c.UserId, c.CustodianUserId, c.Location, c.Note, c.Sha256, c.CreatedAt)).ToList());
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
    public const string EvidenceAdded = "EvidenceAdded";
    public const string EvidenceHandedOver = "EvidenceHandedOver";
    public const string EvidenceIntegrityFailed = "EvidenceIntegrityFailed";
}
