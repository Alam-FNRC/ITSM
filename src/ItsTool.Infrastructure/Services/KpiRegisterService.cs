using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Kpis;
using ItsTool.Domain.Entities.Notification;
using ItsTool.Infrastructure.Data;
using ItsTool.Infrastructure.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// The IT KPI register (feature spec, "IT KPI register"), kept as small as monitoring needs: an administrator
/// (kpi.manage) defines the KPIs, owners enter values, no approval. Changes are audited by SystemAuditInterceptor.
/// </summary>
public class KpiRegisterService : IKpiRegisterService
{
    public const string SkipAlreadyInRegister = "AlreadyInRegister";
    public const string SkipNoName = "NoName";
    public const string DueNotification = "kpi.due";
    private const int MaxReportYears = 10;

    private readonly ItsToolDbContext _context;
    private readonly IPermissionCalculator _permissions;
    private readonly IKpiService? _tickets;
    private readonly ISignalRPusher? _pusher;
    private readonly TimeZoneInfo _zone;

    public KpiRegisterService(ItsToolDbContext context, IPermissionCalculator permissions, IConfiguration? config = null, IKpiService? tickets = null, ISignalRPusher? pusher = null)
    {
        _context = context;
        _permissions = permissions;
        _tickets = tickets;
        _pusher = pusher;
        _zone = BusinessCalendar.ResolveTimeZone(config?[BusinessCalendar.TimeZoneSetting]);
    }

    private async Task<bool> CanManageAsync(int userId) =>
        (await _permissions.CalculateEffectivePermissionsAsync(userId)).Contains(PermissionConstants.KpiManage);

    private async Task RequireManageAsync(int userId)
    {
        if (!await CanManageAsync(userId)) throw new UnauthorizedAccessException();
    }

    /// <summary>A KPI the caller may see, or "not found".</summary>
    private async Task<(KpiDefinition Kpi, bool Manage)> LoadAsync(int userId, int kpiId)
    {
        var kpi = await _context.KpiDefinitions.FirstOrDefaultAsync(k => k.Id == kpiId && !k.IsDeleted) ?? throw new KeyNotFoundException();
        var manage = await CanManageAsync(userId);
        if (!manage && kpi.OwnerUserId != userId) throw new KeyNotFoundException();
        return (kpi, manage);
    }

    private DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, _zone));

    // ---- Reading ----

    public async Task<KpiRegisterDto> ListAsync(int userId, int? year)
    {
        var manage = await CanManageAsync(userId);
        var today = Today(DateTime.UtcNow);
        var y = year ?? today.Year;
        if (!KpiPeriods.IsYear(y)) throw new ArgumentException("The year is out of range.");

        var query = _context.KpiDefinitions.AsNoTracking().Where(k => !k.IsDeleted);
        if (!manage) query = query.Where(k => k.OwnerUserId == userId);
        var kpis = await query.OrderBy(k => k.Number).ToListAsync();
        var ids = kpis.Select(k => k.Id).ToList();
        var values = await _context.KpiValues.AsNoTracking().Where(v => ids.Contains(v.KpiId) && v.Year == y).ToListAsync();
        var targets = await _context.KpiTargets.AsNoTracking().Where(t => ids.Contains(t.KpiId) && t.Year == y).ToListAsync();
        var capabilityIds = kpis.Select(k => k.CapabilityId).ToHashSet();
        var capabilities = await _context.KpiCapabilities.AsNoTracking().Where(c => !c.IsDeleted && (manage || capabilityIds.Contains(c.Id))).OrderBy(c => c.Number).ToListAsync();
        var names = await UserNamesAsync(kpis.Where(k => k.OwnerUserId.HasValue).Select(k => k.OwnerUserId!.Value));
        var computed = await ComputedAsync(kpis, y, y, today);

        return new KpiRegisterDto(y,
            capabilities.Select(ToDto).ToList(),
            kpis.Select(k =>
            {
                var stored = values.Where(v => v.KpiId == k.Id).ToList();
                return new KpiListItemDto(
                    k.Id, k.Number, k.CapabilityId, k.NameEn, k.NameAr, k.ResultType.ToString(), k.Direction.ToString(), k.Cycle.ToString(),
                    k.YearEnd.ToString(), k.Source.ToString(), k.Unit, k.OwnerUserId,
                    k.OwnerUserId.HasValue ? names.GetValueOrDefault(k.OwnerUserId.Value) : null,
                    YearEnd(k, y, stored, computed), targets.FirstOrDefault(t => t.KpiId == k.Id)?.Value,
                    stored.Count, KpiPeriods.Completed(k.Cycle, y, today), manage || k.OwnerUserId == userId);
            }).ToList(),
            manage);
    }

    public async Task<KpiDetailDto> GetAsync(int userId, int kpiId)
    {
        var (kpi, manage) = await LoadAsync(userId, kpiId);
        var today = Today(DateTime.UtcNow);
        var values = await _context.KpiValues.AsNoTracking().Where(v => v.KpiId == kpiId).OrderBy(v => v.Year).ThenBy(v => v.Period).ToListAsync();
        var targets = await _context.KpiTargets.AsNoTracking().Where(t => t.KpiId == kpiId).OrderBy(t => t.Year).ToListAsync();
        var names = await UserNamesAsync(values.Select(v => v.EnteredByUserId).Concat(kpi.OwnerUserId.HasValue ? new[] { kpi.OwnerUserId.Value } : Array.Empty<int>()));

        // Figures from the tickets for this year and the last, where nothing was typed
        var computed = await ComputedAsync(new[] { kpi }, today.Year - 1, today.Year, today);
        var list = values.Select(v => new KpiValueDto(v.Year, v.Cycle.ToString(), v.Period, v.Value, v.Note, v.EnteredByUserId, v.UpdatedAt ?? v.CreatedAt, false)).ToList();
        list.AddRange(computed
            .Where(c => !values.Any(v => v.Year == c.Key.Year && v.Cycle == kpi.Cycle && v.Period == c.Key.Period))
            .Select(c => new KpiValueDto(c.Key.Year, kpi.Cycle.ToString(), c.Key.Period, c.Value, null, null, null, true)));

        return new KpiDetailDto(
            kpi.Id, kpi.Number, kpi.CapabilityId, kpi.NameEn, kpi.NameAr, kpi.Description, kpi.Formula, kpi.DataSource, kpi.Scope, kpi.Unit,
            kpi.ResultType.ToString(), kpi.Direction.ToString(), kpi.Cycle.ToString(), kpi.YearEnd.ToString(), kpi.Source.ToString(), kpi.OwnerUserId,
            list.OrderBy(v => v.Year).ThenBy(v => v.Period).ToList(), targets.Select(t => new KpiTargetDto(t.Year, t.Value)).ToList(),
            names, manage, manage || kpi.OwnerUserId == userId, kpi.CreatedAt);
    }

    // ---- Keeping the register ----

    public async Task<KpiDetailDto> CreateAsync(int userId, SaveKpiDto dto)
    {
        await RequireManageAsync(userId);
        var kpi = new KpiDefinition();
        await ApplyAsync(kpi, dto);
        _context.KpiDefinitions.Add(kpi);
        await _context.SaveChangesAsync();
        return await GetAsync(userId, kpi.Id);
    }

    public async Task UpdateAsync(int userId, int kpiId, SaveKpiDto dto)
    {
        var (kpi, manage) = await LoadAsync(userId, kpiId);
        if (!manage) throw new UnauthorizedAccessException();
        await ApplyAsync(kpi, dto);
        kpi.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    private async Task ApplyAsync(KpiDefinition kpi, SaveKpiDto dto)
    {
        if (await _context.KpiDefinitions.AnyAsync(k => k.Id != kpi.Id && k.Number == dto.Number && !k.IsDeleted))
            throw new ArgumentException($"KPI number {dto.Number} is already used.");
        if (!await _context.KpiCapabilities.AnyAsync(c => c.Id == dto.CapabilityId && !c.IsDeleted))
            throw new ArgumentException("Choose a capability.");
        if (dto.OwnerUserId is { } owner && !await _context.Users.AnyAsync(u => u.Id == owner && u.IsActive && !u.IsDeleted))
            throw new ArgumentException("The owner is not an active user.");

        kpi.Number = dto.Number;
        kpi.CapabilityId = dto.CapabilityId;
        kpi.NameEn = Required(dto.NameEn, "English name");
        kpi.NameAr = Required(dto.NameAr, "Arabic name");
        kpi.Description = Trimmed(dto.Description);
        kpi.Formula = Trimmed(dto.Formula);
        kpi.DataSource = Trimmed(dto.DataSource);
        kpi.Scope = Trimmed(dto.Scope);
        kpi.Unit = Trimmed(dto.Unit);
        kpi.ResultType = dto.ResultType == null ? KpiResultType.Number : EnumNames.Parse<KpiResultType>(dto.ResultType, "Result type");
        kpi.Direction = dto.Direction == null ? KpiDirection.Up : EnumNames.Parse<KpiDirection>(dto.Direction, "Direction");
        kpi.Cycle = dto.Cycle == null ? KpiCycle.Yearly : EnumNames.Parse<KpiCycle>(dto.Cycle, "Cycle");
        kpi.YearEnd = dto.YearEnd == null ? DefaultYearEnd(kpi.ResultType) : EnumNames.Parse<KpiYearEnd>(dto.YearEnd, "Year-end rule");
        kpi.Source = dto.Source == null ? KpiSource.Manual : EnumNames.Parse<KpiSource>(dto.Source, "Source");
        kpi.OwnerUserId = dto.OwnerUserId;
    }

    public static KpiYearEnd DefaultYearEnd(KpiResultType type) => type == KpiResultType.Percentage ? KpiYearEnd.Average : KpiYearEnd.Last;

    public async Task<KpiCapabilityDto> SaveCapabilityAsync(int userId, int? capabilityId, SaveKpiCapabilityDto dto)
    {
        await RequireManageAsync(userId);
        var capability = capabilityId == null
            ? _context.KpiCapabilities.Add(new KpiCapability()).Entity
            : await _context.KpiCapabilities.FirstOrDefaultAsync(c => c.Id == capabilityId && !c.IsDeleted) ?? throw new KeyNotFoundException();
        if (await _context.KpiCapabilities.AnyAsync(c => c.Id != capability.Id && c.Number == dto.Number && !c.IsDeleted))
            throw new ArgumentException($"Capability number {dto.Number} is already used.");
        capability.Number = dto.Number;
        capability.NameEn = Required(dto.NameEn, "English name");
        capability.NameAr = Required(dto.NameAr, "Arabic name");
        if (capabilityId != null) capability.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return ToDto(capability);
    }

    // ---- Values and targets ----

    public async Task SaveValueAsync(int userId, int kpiId, int year, int period, SaveKpiValueDto dto)
    {
        var (kpi, manage) = await LoadAsync(userId, kpiId);
        if (!manage && kpi.OwnerUserId != userId) throw new UnauthorizedAccessException();
        if (!KpiPeriods.IsYear(year)) throw new ArgumentException("The year is out of range.");
        if (!KpiPeriods.IsValid(kpi.Cycle, period)) throw new ArgumentException($"Period {period} does not exist for a {kpi.Cycle} KPI.");
        if (KpiPeriods.Start(kpi.Cycle, year, period) > Today(DateTime.UtcNow)) throw new ArgumentException("That period has not started yet.");

        var row = await _context.KpiValues.FirstOrDefaultAsync(v => v.KpiId == kpiId && v.Year == year && v.Cycle == kpi.Cycle && v.Period == period);
        if (dto.Value == null)
        {
            if (row != null) _context.KpiValues.Remove(row);
        }
        else
        {
            if (Math.Abs(dto.Value.Value) >= 1_000_000_000_000m) throw new ArgumentException("The value is too large.");
            if (row == null)
            {
                row = new KpiValue { KpiId = kpiId, Year = year, Cycle = kpi.Cycle, Period = period };
                _context.KpiValues.Add(row);
            }
            else row.UpdatedAt = DateTime.UtcNow;
            row.Value = dto.Value.Value;
            row.Note = Trimmed(dto.Note);
            row.EnteredByUserId = userId;
        }
        await _context.SaveChangesAsync();
    }

    public async Task SaveTargetAsync(int userId, int kpiId, int year, SaveKpiTargetDto dto)
    {
        var (_, manage) = await LoadAsync(userId, kpiId);
        if (!manage) throw new UnauthorizedAccessException();
        if (!KpiPeriods.IsYear(year)) throw new ArgumentException("The year is out of range.");

        var row = await _context.KpiTargets.FirstOrDefaultAsync(t => t.KpiId == kpiId && t.Year == year);
        if (dto.Value == null)
        {
            if (row != null) _context.KpiTargets.Remove(row);
        }
        else
        {
            if (row == null)
            {
                row = new KpiTarget { KpiId = kpiId, Year = year };
                _context.KpiTargets.Add(row);
            }
            else row.UpdatedAt = DateTime.UtcNow;
            row.Value = dto.Value.Value;
        }
        await _context.SaveChangesAsync();
    }

    // ---- The year-end report ----

    public async Task<KpiYearReportDto> ReportAsync(int userId, int fromYear, int toYear)
    {
        await RequireManageAsync(userId);
        if (!KpiPeriods.IsYear(fromYear) || !KpiPeriods.IsYear(toYear) || fromYear > toYear) throw new ArgumentException("The years are out of range.");
        if (toYear - fromYear + 1 > MaxReportYears) throw new ArgumentException($"A report covers at most {MaxReportYears} years.");

        var today = Today(DateTime.UtcNow);
        var kpis = await _context.KpiDefinitions.AsNoTracking().Where(k => !k.IsDeleted).OrderBy(k => k.Number).ToListAsync();
        var capabilities = await _context.KpiCapabilities.AsNoTracking().Where(c => !c.IsDeleted).OrderBy(c => c.Number).ToListAsync();
        var values = await _context.KpiValues.AsNoTracking().Where(v => v.Year >= fromYear && v.Year <= toYear).ToListAsync();
        var targets = await _context.KpiTargets.AsNoTracking().Where(t => t.Year >= fromYear && t.Year <= toYear).ToListAsync();
        var names = await UserNamesAsync(kpis.Where(k => k.OwnerUserId.HasValue).Select(k => k.OwnerUserId!.Value));
        var computed = await ComputedAsync(kpis, fromYear, toYear, today);
        var years = Enumerable.Range(fromYear, toYear - fromYear + 1).ToList();

        return new KpiYearReportDto(fromYear, toYear, DateTime.UtcNow, capabilities.Select(ToDto).ToList(),
            kpis.Select(k => new KpiReportKpiDto(
                k.Id, k.Number, k.CapabilityId, k.NameEn, k.NameAr, k.Unit, k.ResultType.ToString(), k.Direction.ToString(), k.Cycle.ToString(),
                k.OwnerUserId.HasValue ? names.GetValueOrDefault(k.OwnerUserId.Value) : null,
                years.ToDictionary(y => y, y => YearEnd(k, y, values.Where(v => v.KpiId == k.Id && v.Year == y).ToList(), computed)),
                years.ToDictionary(y => y, y => targets.FirstOrDefault(t => t.KpiId == k.Id && t.Year == y)?.Value),
                years.ToDictionary(y => y, y => values.Count(v => v.KpiId == k.Id && v.Year == y)))).ToList());
    }

    /// <summary>The year's figure from the stored values, with the tickets' figures filling periods nobody typed.</summary>
    private static decimal? YearEnd(KpiDefinition kpi, int year, List<KpiValue> stored, Dictionary<(int Kpi, int Year, int Period), decimal> computed)
    {
        var values = stored.Select(v => (v.Cycle, v.Period, v.Value)).ToList();
        values.AddRange(computed.Where(c => c.Key.Kpi == kpi.Id && c.Key.Year == year && !stored.Any(v => v.Cycle == kpi.Cycle && v.Period == c.Key.Period))
            .Select(c => (kpi.Cycle, c.Key.Period, c.Value)));
        return KpiPeriods.YearEnd(kpi.YearEnd, year, values);
    }

    /// <summary>
    /// Average fault resolution time in working hours from this system's tickets, per period that has started, for
    /// the KPIs whose source is the tickets. Nothing when no ticket service is available (unit tests).
    /// </summary>
    private async Task<Dictionary<(int Kpi, int Year, int Period), decimal>> ComputedAsync(IEnumerable<KpiDefinition> kpis, int fromYear, int toYear, DateOnly today)
    {
        var result = new Dictionary<(int, int, int), decimal>();
        if (_tickets == null) return result;
        foreach (var kpi in kpis.Where(k => k.Source == KpiSource.TicketResolutionTime))
        {
            for (var year = Math.Max(fromYear, KpiPeriods.FirstYear); year <= toYear; year++)
            {
                for (var period = 1; period <= KpiPeriods.Count(kpi.Cycle); period++)
                {
                    var start = KpiPeriods.Start(kpi.Cycle, year, period);
                    if (start > today) continue;
                    var end = KpiPeriods.End(kpi.Cycle, year, period);
                    var report = await _tickets.GetReportAsync(new KpiFilterDto { From = start, To = end < today ? end : today });
                    if (report?.Summary?.MeanTimeToResolveMinutes is { } minutes) result[(kpi.Id, year, period)] = Math.Round((decimal)minutes / 60, 1, MidpointRounding.AwayFromZero);
                }
            }
        }
        return result;
    }

    // ---- Import ----

    public async Task<KpiImportPreviewDto> PreviewImportAsync(int userId, Stream workbook)
    {
        await RequireManageAsync(userId);
        var plan = await PlanImportAsync(KpiWorkbook.Parse(workbook));
        return new KpiImportPreviewDto(plan.Select(p => new KpiImportRowDto(
            p.Row.RowNumber, p.Row.Number, p.Row.NameAr, p.Row.NameEn ?? p.Row.NameAr, p.Row.CapabilityNumber, p.Row.CapabilityNameAr,
            p.Row.ResultType.ToString(), p.Row.Direction.ToString(), p.Row.Cycle.ToString(), p.Row.Values, p.Row.Warnings, p.Skip)).ToList());
    }

    public async Task<KpiImportResultDto> ImportAsync(int userId, Stream workbook)
    {
        await RequireManageAsync(userId);
        var plan = await PlanImportAsync(KpiWorkbook.Parse(workbook));
        var capabilities = await _context.KpiCapabilities.Where(c => !c.IsDeleted).ToListAsync();
        var taken = (await _context.KpiDefinitions.Where(k => !k.IsDeleted).Select(k => k.Number).ToListAsync()).ToHashSet();
        var nextNumber = taken.Count == 0 ? 1 : taken.Max() + 1;
        var imported = 0;

        foreach (var (row, skip) in plan)
        {
            if (skip != null) continue;
            var capability = capabilities.FirstOrDefault(c => c.Number == (row.CapabilityNumber ?? 0));
            if (capability == null)
            {
                var nameAr = row.CapabilityNameAr ?? $"Capability {row.CapabilityNumber}";
                capability = new KpiCapability { Number = row.CapabilityNumber ?? (capabilities.Count == 0 ? 1 : capabilities.Max(c => c.Number) + 1), NameAr = nameAr, NameEn = KpiNames.Of(nameAr) ?? nameAr };
                _context.KpiCapabilities.Add(capability);
                capabilities.Add(capability);
            }
            var number = row.Number is { } n && taken.Add(n) ? n : nextNumber++;
            while (!taken.Add(nextNumber)) nextNumber++;
            var kpi = new KpiDefinition
            {
                Number = number, Capability = capability, NameAr = row.NameAr, NameEn = row.NameEn ?? row.NameAr,
                Description = Cut(row.Description, 2000), Formula = Cut(row.Formula, 1000), DataSource = Cut(row.DataSource, 500), Scope = Cut(row.Scope, 100), Unit = Cut(row.Unit, 100),
                ResultType = row.ResultType, Direction = row.Direction, Cycle = row.Cycle, YearEnd = DefaultYearEnd(row.ResultType), Source = KpiSource.Manual
            };
            foreach (var (year, value) in row.Values)
            {
                kpi.Values.Add(new KpiValue { Year = year, Cycle = KpiCycle.Yearly, Period = 1, Value = value, EnteredByUserId = userId, Note = $"Excel row {row.RowNumber}" });
            }
            _context.KpiDefinitions.Add(kpi);
            imported++;
        }
        await _context.SaveChangesAsync();
        return new KpiImportResultDto(imported, plan.Count - imported);
    }

    private sealed record PlannedRow(KpiWorkbook.Row Row, string? Skip);

    /// <summary>A row whose KPI number is already in the register was imported before.</summary>
    private async Task<List<PlannedRow>> PlanImportAsync(IReadOnlyList<KpiWorkbook.Row> rows)
    {
        var taken = (await _context.KpiDefinitions.Where(k => !k.IsDeleted).Select(k => k.Number).ToListAsync()).ToHashSet();
        return rows.Select(row => new PlannedRow(row,
            string.IsNullOrWhiteSpace(row.NameAr) ? SkipNoName :
            row.Number is { } n && taken.Contains(n) ? SkipAlreadyInRegister : null)).ToList();
    }

    // ---- Owners and reminders ----

    public Task<bool> OwnsAnyAsync(int userId) => _context.KpiDefinitions.AnyAsync(k => k.OwnerUserId == userId && !k.IsDeleted);

    public async Task<int> SendRemindersAsync(DateTime utcNow)
    {
        var today = Today(utcNow);
        var kpis = await _context.KpiDefinitions.Where(k => !k.IsDeleted && k.OwnerUserId != null && k.Source == KpiSource.Manual).ToListAsync();
        var ownerIds = kpis.Select(k => k.OwnerUserId!.Value).Distinct().ToList();
        var activeOwners = (await _context.Users.Where(u => ownerIds.Contains(u.Id) && u.IsActive && !u.IsDeleted).Select(u => u.Id).ToListAsync()).ToHashSet();
        var sent = 0;

        foreach (var kpi in kpis)
        {
            var owner = kpi.OwnerUserId!.Value;
            if (!activeOwners.Contains(owner) || KpiPeriods.LastCompleted(kpi.Cycle, today) is not { } last) continue;
            var (year, period) = last;
            if (await _context.KpiValues.AnyAsync(v => v.KpiId == kpi.Id && v.Year == year && v.Cycle == kpi.Cycle && v.Period == period)) continue;
            var key = KpiPeriods.Key(kpi.Cycle, year, period);
            var body = $"KPI {kpi.Number} ({kpi.NameEn}) has no value for {key}.";
            if (await _context.Notifications.AnyAsync(n => n.UserId == owner && n.Type == DueNotification && n.EntityId == kpi.Id && n.Body == body)) continue;
            var notification = new Notification { UserId = owner, Type = DueNotification, Title = "KPI value due", Body = body, EntityType = "Kpi", EntityId = kpi.Id, Priority = "Normal" };
            _context.Notifications.Add(notification);
            _pending.Add(notification);
            sent++;
        }

        await _context.SaveChangesAsync();
        if (_pusher != null)
        {
            foreach (var n in _pending)
            {
                await _pusher.PushNotificationAsync(n.UserId, new { type = n.Type, title = n.Title, body = n.Body, entityType = n.EntityType, entityId = n.EntityId, priority = n.Priority, createdAt = n.CreatedAt });
            }
        }
        _pending.Clear();
        return sent;
    }

    private readonly List<Notification> _pending = new();

    // ---- Helpers ----

    private static KpiCapabilityDto ToDto(KpiCapability c) => new(c.Id, c.Number, c.NameEn, c.NameAr);

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

    private static string? Cut(string? value, int max) => value == null || value.Length <= max ? value : value[..max];
}
