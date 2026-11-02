using System.Globalization;
using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Notification;
using ItsTool.Domain.Entities.Risks;
using ItsTool.Infrastructure.Data;
using ItsTool.Infrastructure.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// The risk register (feature spec, "Risk register"; ISO 27001 6.1.2, 6.1.3, 8.2, 8.3), kept as small as the
/// standard allows: one screen per risk, three statuses and one approval. risk.manage keeps the register,
/// risk.approve approves, and a risk owner sees their own risks; anyone else gets "not found".
/// </summary>
public class RiskService : IRiskService
{
    private const int NumberAttempts = 3;
    public const int ReviewIntervalDays = 365;
    public const int DueSoonDays = 14;
    private const int ReviewReminderIntervalDays = 30;
    private const int MaxText = 2000;

    private readonly ItsToolDbContext _context;
    private readonly IPermissionCalculator _permissions;
    private readonly ISignalRPusher? _pusher;
    private readonly TimeZoneInfo _zone;

    public RiskService(ItsToolDbContext context, IPermissionCalculator permissions, IConfiguration? config = null, ISignalRPusher? pusher = null)
    {
        _context = context;
        _permissions = permissions;
        _pusher = pusher;
        _zone = BusinessCalendar.ResolveTimeZone(config?[BusinessCalendar.TimeZoneSetting]);
    }

    private sealed record Rights(bool Manage, bool Approve)
    {
        public bool ViewAll => Manage || Approve;
    }

    private async Task<Rights> GetRightsAsync(int userId)
    {
        var perms = await _permissions.CalculateEffectivePermissionsAsync(userId);
        return new Rights(perms.Contains(PermissionConstants.RiskManage), perms.Contains(PermissionConstants.RiskApprove));
    }

    private async Task<Rights> RequireManageAsync(int userId)
    {
        var rights = await GetRightsAsync(userId);
        return rights.Manage ? rights : throw new UnauthorizedAccessException();
    }

    /// <summary>A risk the caller may see, or "not found".</summary>
    private async Task<(Risk Risk, Rights Rights)> LoadAsync(int userId, int riskId)
    {
        var risk = await _context.Risks.FirstOrDefaultAsync(r => r.Id == riskId && !r.IsDeleted) ?? throw new KeyNotFoundException();
        var rights = await GetRightsAsync(userId);
        if (!rights.ViewAll && risk.OwnerUserId != userId) throw new KeyNotFoundException();
        return (risk, rights);
    }

    private static void Record(Risk risk, int userId, string kind, string? field = null, string? oldValue = null, string? newValue = null, string? note = null) =>
        risk.History.Add(new RiskHistory { RiskId = risk.Id, UserId = userId, Kind = kind, Field = field, OldValue = Cut(oldValue), NewValue = Cut(newValue), Note = Cut(note) });

    private DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, _zone));

    private static bool IsOverdue(Risk r, DateOnly today) => r.Status != RiskStatus.Done && r.TargetDate < today;

    private static bool IsReviewDue(Risk r, DateTime utcNow) => (r.LastReviewedAt ?? r.CreatedAt) <= utcNow.AddDays(-ReviewIntervalDays);

    // ---- Reading ----

    public async Task<RiskRegisterDto> ListAsync(int userId)
    {
        var rights = await GetRightsAsync(userId);
        var query = _context.Risks.AsNoTracking().Where(r => !r.IsDeleted);
        if (!rights.ViewAll) query = query.Where(r => r.OwnerUserId == userId);
        var risks = await query.OrderBy(r => r.Number).ToListAsync();
        var names = await UserNamesAsync(risks.Where(r => r.OwnerUserId.HasValue).Select(r => r.OwnerUserId!.Value));
        var now = DateTime.UtcNow;
        var today = Today(now);

        return new RiskRegisterDto(risks.Select(r =>
        {
            var score = RiskScore.Of(r);
            var residual = RiskScore.ResidualOf(r);
            return new RiskListItemDto(
                r.Id, r.Number, r.Title, score, RiskScore.Level(score), residual, residual.HasValue ? RiskScore.Level(residual.Value) : null,
                r.Treatment.ToString(), r.Status.ToString(), r.OwnerUserId,
                r.OwnerUserId.HasValue ? names.GetValueOrDefault(r.OwnerUserId.Value) : null,
                r.TargetDate, IsOverdue(r, today), r.ApprovedAt.HasValue, r.LastReviewedAt, IsReviewDue(r, now));
        }).ToList(), rights.Manage, rights.Approve);
    }

    public async Task<RiskDetailDto> GetAsync(int userId, int riskId)
    {
        var (risk, rights) = await LoadAsync(userId, riskId);
        var history = await _context.RiskHistory.AsNoTracking().Where(h => h.RiskId == riskId).OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).ToListAsync();

        // Owner changes in the history name users by id
        var userIds = history.Select(h => h.UserId)
            .Concat(history.Where(h => h.Field == "Owner").SelectMany(h => new[] { h.OldValue, h.NewValue }).Select(v => int.TryParse(v, out var id) ? id : 0).Where(id => id > 0))
            .Concat(new[] { risk.OwnerUserId, risk.ApprovedByUserId, risk.LastReviewedByUserId }.Where(id => id.HasValue).Select(id => id!.Value));
        var names = await UserNamesAsync(userIds);
        var now = DateTime.UtcNow;
        var score = RiskScore.Of(risk);
        var residual = RiskScore.ResidualOf(risk);

        return new RiskDetailDto(
            risk.Id, risk.Number, risk.Title, risk.Threat, risk.Vulnerability, risk.ExistingControls,
            risk.ImpactConfidentiality, risk.ImpactIntegrity, risk.ImpactAvailability, risk.Likelihood, score, RiskScore.Level(score),
            risk.Treatment.ToString(), risk.RecommendedControls, AnnexAControls.Split(risk.AnnexAControls), risk.OwnerUserId, risk.TargetDate, risk.Status.ToString(),
            risk.ResidualConfidentiality, risk.ResidualIntegrity, risk.ResidualAvailability, risk.ResidualLikelihood,
            residual, residual.HasValue ? RiskScore.Level(residual.Value) : null,
            risk.ApprovedAt, risk.ApprovedByUserId, risk.LastReviewedAt, risk.LastReviewedByUserId,
            IsOverdue(risk, Today(now)), IsReviewDue(risk, now), risk.CreatedAt,
            history.Select(h => new RiskHistoryDto(h.Id, h.UserId, h.Kind, h.Field, h.OldValue, h.NewValue, h.Note, h.CreatedAt)).ToList(),
            names, rights.Manage, rights.Approve);
    }

    // ---- Keeping the register ----

    public async Task<RiskDetailDto> CreateAsync(int userId, SaveRiskDto dto)
    {
        await RequireManageAsync(userId);
        var risk = new Risk();
        await ApplyAsync(risk, dto, userId, recordChanges: false);
        Record(risk, userId, RiskActions.Created);
        await SaveNewAsync(risk);
        return await GetAsync(userId, risk.Id);
    }

    public async Task UpdateAsync(int userId, int riskId, SaveRiskDto dto)
    {
        var (risk, rights) = await LoadAsync(userId, riskId);
        if (!rights.Manage) throw new UnauthorizedAccessException();
        await ApplyAsync(risk, dto, userId, recordChanges: true);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Copies the form onto the risk, recording each change. A change to the ratings or the treatment plan
    /// clears an earlier approval, since what was approved is no longer what the risk says.
    /// </summary>
    private async Task ApplyAsync(Risk risk, SaveRiskDto dto, int userId, bool recordChanges)
    {
        var title = Required(dto.Title, "Title");
        foreach (var (rating, field) in new[] { (dto.ImpactConfidentiality, "Confidentiality"), (dto.ImpactIntegrity, "Integrity"), (dto.ImpactAvailability, "Availability"), (dto.Likelihood, "Likelihood") })
        {
            if (!RiskScore.IsRating(rating)) throw new ArgumentException($"{field} must be rated 1 to 5.");
        }
        var residual = new[] { dto.ResidualConfidentiality, dto.ResidualIntegrity, dto.ResidualAvailability, dto.ResidualLikelihood };
        if (residual.Any(r => r.HasValue) && (!residual.All(r => r.HasValue) || residual.Any(r => !RiskScore.IsRating(r!.Value))))
            throw new ArgumentException("Give all four residual ratings (1 to 5), or none.");
        var treatment = string.IsNullOrWhiteSpace(dto.Treatment) ? RiskTreatment.Mitigate : EnumNames.Parse<RiskTreatment>(dto.Treatment, "Treatment");
        var status = string.IsNullOrWhiteSpace(dto.Status) ? RiskStatus.Open : EnumNames.Parse<RiskStatus>(dto.Status, "Status");
        var unknown = (dto.AnnexAControls ?? Array.Empty<string>()).FirstOrDefault(c => !AnnexAControls.Exists(c?.Trim() ?? string.Empty));
        if (unknown != null) throw new ArgumentException($"Annex A control '{unknown}' does not exist.");
        var controls = AnnexAControls.Join((dto.AnnexAControls ?? Array.Empty<string>()).Select(c => c.Trim()));
        if (dto.OwnerUserId.HasValue && dto.OwnerUserId != risk.OwnerUserId &&
            !await _context.Users.AnyAsync(u => u.Id == dto.OwnerUserId && u.IsActive && !u.IsDeleted))
            throw new ArgumentException("Choose an active user as the risk owner.");

        var planChanged = false;
        void Set<T>(string field, T current, T value, Action<T> assign, bool plan)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            if (recordChanges) Record(risk, userId, RiskActions.Updated, field, Text(current), Text(value));
            assign(value);
            planChanged |= plan;
        }

        Set("Title", risk.Title, title, v => risk.Title = v, plan: false);
        Set("Threat", risk.Threat, Trimmed(dto.Threat), v => risk.Threat = v, plan: false);
        Set("Vulnerability", risk.Vulnerability, Trimmed(dto.Vulnerability), v => risk.Vulnerability = v, plan: false);
        Set("ExistingControls", risk.ExistingControls, Trimmed(dto.ExistingControls), v => risk.ExistingControls = v, plan: false);
        Set("Confidentiality", risk.ImpactConfidentiality, dto.ImpactConfidentiality, v => risk.ImpactConfidentiality = v, plan: true);
        Set("Integrity", risk.ImpactIntegrity, dto.ImpactIntegrity, v => risk.ImpactIntegrity = v, plan: true);
        Set("Availability", risk.ImpactAvailability, dto.ImpactAvailability, v => risk.ImpactAvailability = v, plan: true);
        Set("Likelihood", risk.Likelihood, dto.Likelihood, v => risk.Likelihood = v, plan: true);
        Set("Treatment", risk.Treatment, treatment, v => risk.Treatment = v, plan: true);
        Set("RecommendedControls", risk.RecommendedControls, Trimmed(dto.RecommendedControls), v => risk.RecommendedControls = v, plan: true);
        Set("AnnexAControls", risk.AnnexAControls, controls, v => risk.AnnexAControls = v, plan: true);
        Set("Owner", risk.OwnerUserId, dto.OwnerUserId, v => risk.OwnerUserId = v, plan: false);
        Set("TargetDate", risk.TargetDate, dto.TargetDate, v => { risk.TargetDate = v; risk.DueReminderSentAt = null; risk.OverdueReminderSentAt = null; }, plan: true);
        Set("Status", risk.Status, status, v => risk.Status = v, plan: false);
        Set("ResidualConfidentiality", risk.ResidualConfidentiality, dto.ResidualConfidentiality, v => risk.ResidualConfidentiality = v, plan: true);
        Set("ResidualIntegrity", risk.ResidualIntegrity, dto.ResidualIntegrity, v => risk.ResidualIntegrity = v, plan: true);
        Set("ResidualAvailability", risk.ResidualAvailability, dto.ResidualAvailability, v => risk.ResidualAvailability = v, plan: true);
        Set("ResidualLikelihood", risk.ResidualLikelihood, dto.ResidualLikelihood, v => risk.ResidualLikelihood = v, plan: true);

        if (planChanged && risk.ApprovedAt.HasValue)
        {
            Record(risk, userId, RiskActions.ApprovalCleared);
            risk.ApprovedAt = null;
            risk.ApprovedByUserId = null;
        }
    }

    /// <summary>ISO 27001 6.1.3 f: the approver accepts the treatment plan, or the risk itself. Already approved risks are left as they are.</summary>
    public async Task<int> ApproveAsync(int userId, IReadOnlyList<int> riskIds)
    {
        var rights = await GetRightsAsync(userId);
        if (!rights.Approve) throw new UnauthorizedAccessException();
        var risks = await _context.Risks.Where(r => riskIds.Contains(r.Id) && !r.IsDeleted && r.ApprovedAt == null).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var risk in risks)
        {
            risk.ApprovedAt = now;
            risk.ApprovedByUserId = userId;
            Record(risk, userId, RiskActions.Approved, "Treatment", null, risk.Treatment.ToString(), Snapshot(risk));
        }
        await _context.SaveChangesAsync();
        return risks.Count;
    }

    /// <summary>The yearly review: the date, the reviewer and the scores at that moment go into the history.</summary>
    public async Task<int> MarkReviewedAsync(int userId, IReadOnlyList<int> riskIds)
    {
        await RequireManageAsync(userId);
        var risks = await _context.Risks.Where(r => riskIds.Contains(r.Id) && !r.IsDeleted).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var risk in risks)
        {
            risk.LastReviewedAt = now;
            risk.LastReviewedByUserId = userId;
            Record(risk, userId, RiskActions.Reviewed, note: Snapshot(risk));
        }
        await _context.SaveChangesAsync();
        return risks.Count;
    }

    /// <summary>The ratings and scores as plain text, e.g. "C3 I5 A5 L4 = 17.33 (High); residual C3 I4 A5 L1 = 4 (Low)".</summary>
    internal static string Snapshot(Risk risk)
    {
        var score = RiskScore.Of(risk);
        var text = $"C{risk.ImpactConfidentiality} I{risk.ImpactIntegrity} A{risk.ImpactAvailability} L{risk.Likelihood} = {Number(score)} ({RiskScore.Level(score)})";
        return RiskScore.ResidualOf(risk) is { } residual
            ? $"{text}; residual C{risk.ResidualConfidentiality} I{risk.ResidualIntegrity} A{risk.ResidualAvailability} L{risk.ResidualLikelihood} = {Number(residual)} ({RiskScore.Level(residual)})"
            : text;
    }

    // ---- Import from the Excel register ----

    public const string SkipAlreadyInRegister = "AlreadyInRegister";
    public const string SkipNoRatings = "NoRatings";

    public async Task<RiskImportPreviewDto> PreviewImportAsync(int userId, Stream workbook)
    {
        await RequireManageAsync(userId);
        var plan = await PlanImportAsync(RiskWorkbook.Parse(workbook));
        var owners = plan.Where(p => p.Row.Owner != null).Select(p => p.Row.Owner!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o).ToList();
        return new RiskImportPreviewDto(plan.Select(p =>
        {
            var score = p.Row.HasRatings ? RiskScore.Of(p.Row.Confidentiality!.Value, p.Row.Integrity!.Value, p.Row.Availability!.Value, p.Row.Likelihood!.Value) : 0;
            return new RiskImportRowDto(p.Row.RowNumber, p.Number, p.Row.Title, score, p.Row.HasRatings ? RiskScore.Level(score) : "-",
                p.Row.Treatment.ToString(), p.Row.Status.ToString(), p.Row.Owner, p.Row.TargetDate, p.Row.AnnexAControls, p.Row.Warnings, p.Skip);
        }).ToList(), owners);
    }

    public async Task<RiskImportResultDto> ImportAsync(int userId, Stream workbook, IReadOnlyDictionary<string, int> owners)
    {
        await RequireManageAsync(userId);
        var ownerIds = owners.Values.Distinct().ToList();
        var active = await _context.Users.Where(u => ownerIds.Contains(u.Id) && u.IsActive && !u.IsDeleted).Select(u => u.Id).ToListAsync();
        if (active.Count != ownerIds.Count) throw new ArgumentException("Choose active users as risk owners.");
        var ownerOf = new Dictionary<string, int>(owners, StringComparer.OrdinalIgnoreCase);

        var plan = await PlanImportAsync(RiskWorkbook.Parse(workbook));
        var next = Math.Max(await _context.Risks.MaxAsync(r => (int?)r.Number) ?? 0, plan.Max(p => p.Number) ?? 0) + 1;
        var imported = 0;
        foreach (var (row, number, skip) in plan)
        {
            if (skip != null) continue;
            var risk = new Risk
            {
                Number = number ?? next++,
                Title = Cut(row.Title, 300)!,
                Threat = Cut(row.Threat, 1000),
                Vulnerability = Cut(row.Vulnerability),
                ExistingControls = Cut(row.ExistingControls),
                ImpactConfidentiality = row.Confidentiality!.Value,
                ImpactIntegrity = row.Integrity!.Value,
                ImpactAvailability = row.Availability!.Value,
                Likelihood = row.Likelihood!.Value,
                Treatment = row.Treatment,
                RecommendedControls = Cut(row.RecommendedControls),
                AnnexAControls = AnnexAControls.Join(row.AnnexAControls),
                OwnerUserId = row.Owner != null && ownerOf.TryGetValue(row.Owner, out var owner) ? owner : null,
                TargetDate = row.TargetDate,
                Status = row.Status,
                ResidualConfidentiality = row.ResidualConfidentiality,
                ResidualIntegrity = row.ResidualIntegrity,
                ResidualAvailability = row.ResidualAvailability,
                ResidualLikelihood = row.ResidualLikelihood
            };
            Record(risk, userId, RiskActions.Imported, note: $"Excel row {row.RowNumber}");
            _context.Risks.Add(risk);
            imported++;
        }
        await _context.SaveChangesAsync();
        return new RiskImportResultDto(imported, plan.Count - imported);
    }

    private sealed record PlannedRow(RiskWorkbook.Row Row, int? Number, string? Skip);

    /// <summary>A row keeps its Excel number unless another row in the file took it first; a number already in the register means the row was imported before.</summary>
    private async Task<List<PlannedRow>> PlanImportAsync(IReadOnlyList<RiskWorkbook.Row> rows)
    {
        var taken = (await _context.Risks.Select(r => r.Number).ToListAsync()).ToHashSet();
        var used = new HashSet<int>();
        return rows.Select(row =>
        {
            if (row.Number is { } n && taken.Contains(n)) return new PlannedRow(row, n, SkipAlreadyInRegister);
            if (!row.HasRatings) return new PlannedRow(row, null, SkipNoRatings);
            var number = row.Number is { } wanted && wanted > 0 && used.Add(wanted) ? wanted : (int?)null;
            return new PlannedRow(row, number, null);
        }).ToList();
    }

    // ---- Reminders ----

    public async Task<int> SendRemindersAsync(DateTime utcNow)
    {
        var today = Today(utcNow);
        var managers = await PermissionHolders.FindAsync(_context, PermissionConstants.RiskManage);
        var activeOwners = await _context.Risks.Where(r => r.OwnerUserId != null && !r.IsDeleted)
            .Select(r => r.OwnerUserId!.Value).Distinct()
            .Where(id => _context.Users.Any(u => u.Id == id && u.IsActive && !u.IsDeleted)).ToListAsync();
        List<int> OwnerOr(Risk r, IEnumerable<int> fallback) =>
            r.OwnerUserId is { } owner && activeOwners.Contains(owner) ? new List<int> { owner } : fallback.ToList();

        var sent = 0;
        var dated = await _context.Risks.Where(r => !r.IsDeleted && r.Status != RiskStatus.Done && r.TargetDate != null).ToListAsync();
        foreach (var risk in dated)
        {
            var due = risk.TargetDate!.Value;
            var date = due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (due < today && risk.OverdueReminderSentAt == null)
            {
                sent += Notify(OwnerOr(risk, managers).Concat(managers), risk.Id, "risk.overdue", "Risk treatment overdue",
                    $"The treatment of risk {Reference(risk.Number)} was due on {date}.");
                risk.OverdueReminderSentAt = utcNow;
                risk.DueReminderSentAt ??= utcNow;
            }
            else if (due >= today && due.DayNumber - today.DayNumber <= DueSoonDays && risk.DueReminderSentAt == null)
            {
                sent += Notify(OwnerOr(risk, managers), risk.Id, "risk.due", "Risk treatment due soon",
                    $"The treatment of risk {Reference(risk.Number)} is due on {date}.");
                risk.DueReminderSentAt = utcNow;
            }
        }

        // The yearly review: one reminder a month to the people who keep the register, while any risk is due
        var reviewCutoff = utcNow.AddDays(-ReviewIntervalDays);
        var reviewDue = await _context.Risks.CountAsync(r => !r.IsDeleted && (r.LastReviewedAt ?? r.CreatedAt) <= reviewCutoff);
        if (reviewDue > 0)
        {
            var since = utcNow.AddDays(-ReviewReminderIntervalDays);
            var reminded = await _context.Notifications.Where(n => n.Type == "risk.review" && n.CreatedAt >= since).Select(n => n.UserId).ToListAsync();
            sent += Notify(managers.Except(reminded), 0, "risk.review", "Risk register review due",
                $"{reviewDue} risk(s) are due for their yearly review.");
        }

        await _context.SaveChangesAsync();
        await PushAsync();
        return sent;
    }

    private readonly List<Notification> _pending = new();

    // Notifications name the reference only; the details stay behind the register's access check
    private int Notify(IEnumerable<int> userIds, int riskId, string type, string title, string body)
    {
        var recipients = userIds.Distinct().ToList();
        foreach (var recipient in recipients)
        {
            var notification = new Notification { UserId = recipient, Type = type, Title = title, Body = body, EntityType = "Risk", EntityId = riskId, Priority = "High" };
            _context.Notifications.Add(notification);
            _pending.Add(notification);
        }
        return recipients.Count;
    }

    private async Task PushAsync()
    {
        if (_pusher != null)
        {
            foreach (var n in _pending)
            {
                await _pusher.PushNotificationAsync(n.UserId, new { type = n.Type, title = n.Title, body = n.Body, entityType = n.EntityType, entityId = n.EntityId, priority = n.Priority, createdAt = n.CreatedAt });
            }
        }
        _pending.Clear();
    }

    // ---- Helpers ----

    public static string Reference(int number) => $"R-{number:D3}";

    /// <summary>Numbers run on from the highest; two risks saved at once take the next number on retry.</summary>
    private async Task SaveNewAsync(Risk risk)
    {
        for (var attempt = 1; ; attempt++)
        {
            risk.Number = (await _context.Risks.MaxAsync(r => (int?)r.Number) ?? 0) + attempt;
            if (risk.Id == 0 && _context.Entry(risk).State == EntityState.Detached) _context.Risks.Add(risk);
            try
            {
                await _context.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < NumberAttempts)
            {
                // The number was taken in the meantime; try the next one
            }
        }
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids)
    {
        var wanted = ids.Distinct().ToList();
        return await _context.Users.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim());
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? Text<T>(T value) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field} is required.") : value.Trim();

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Cut(string? value, int max = MaxText) => value == null || value.Length <= max ? value : value[..max];
}

/// <summary>Kinds of entry in a risk's history.</summary>
public static class RiskActions
{
    public const string Created = "Created";
    public const string Imported = "Imported";
    public const string Updated = "Updated";
    public const string Approved = "Approved";
    public const string ApprovalCleared = "ApprovalCleared";
    public const string Reviewed = "Reviewed";
}
