using ItsTool.Domain.Entities.SLA;
using ItsTool.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// Working time for SLA deadlines and ticket time measures: business hours per weekday and holidays, in the
/// service desk's time zone. Business hours and holiday dates are local, so UTC instants are converted first.
/// </summary>
public sealed class BusinessCalendar
{
    public const string TimeZoneSetting = "BusinessCalendar:TimeZone";
    public const string DefaultTimeZoneId = "Asia/Dubai";

    // Stops a calendar whose working days are all holidays from searching for ever
    private const int MaxDaysSearched = 3660;

    private readonly TimeZoneInfo _zone;
    private readonly Dictionary<DayOfWeek, (TimeSpan Start, TimeSpan End)> _hours = new();
    private readonly HashSet<DateTime> _holidays = new();
    private readonly HashSet<(int Month, int Day)> _yearlyHolidays = new();

    public BusinessCalendar(IEnumerable<BusinessHour> businessHours, IEnumerable<Holiday> holidays, TimeZoneInfo zone)
    {
        _zone = zone;
        foreach (var bh in businessHours.Where(b => b.IsWorkingDay && b.IsActive && !b.IsDeleted && b.EndTime > b.StartTime))
        {
            _hours.TryAdd(bh.DayOfWeek, (bh.StartTime, bh.EndTime));
        }
        foreach (var h in holidays.Where(h => h.IsActive && !h.IsDeleted))
        {
            if (h.IsRecurring) _yearlyHolidays.Add((h.Date.Month, h.Date.Day));
            else _holidays.Add(h.Date.Date);
        }
    }

    /// <summary>With no working day configured every minute counts, so deadlines still come out.</summary>
    public bool IsAlwaysOpen => _hours.Count == 0;

    public static async Task<BusinessCalendar> LoadAsync(ItsToolDbContext context, string? timeZoneId)
    {
        var hours = await context.BusinessHours.ToListAsync();
        var holidays = await context.Holidays.ToListAsync();
        return new BusinessCalendar(hours, holidays, ResolveTimeZone(timeZoneId));
    }

    /// <summary>
    /// The configured zone, or Asia/Dubai. A container without time zone data cannot find Asia/Dubai, so it falls
    /// back to a fixed UTC+4 (the UAE has no daylight saving); any other unknown zone is a configuration error.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim();
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        if (id == DefaultTimeZoneId)
            return TimeZoneInfo.CreateCustomTimeZone(DefaultTimeZoneId, TimeSpan.FromHours(4), "Gulf Standard Time", "Gulf Standard Time");
        throw new InvalidOperationException($"Unknown time zone '{id}' in {TimeZoneSetting}.");
    }

    public DateTime AddWorkingMinutes(DateTime startUtc, int minutes)
    {
        startUtc = AsUtc(startUtc);
        if (minutes <= 0) return startUtc;
        if (IsAlwaysOpen) return startUtc.AddMinutes(minutes);

        var local = TimeZoneInfo.ConvertTimeFromUtc(startUtc, _zone);
        var remaining = TimeSpan.FromMinutes(minutes);
        for (var day = 0; day < MaxDaysSearched; day++, local = local.Date.AddDays(1))
        {
            if (!TryGetWindow(local.Date, out var open, out var close)) continue;
            var from = local > open ? local : open;
            if (from >= close) continue;

            var available = close - from;
            if (remaining <= available) return ToUtc(from + remaining);
            remaining -= available;
        }
        throw new InvalidOperationException("The business calendar has no working time in the next ten years.");
    }

    /// <summary>Whole working minutes between two instants; zero when the end is not after the start.</summary>
    public int WorkingMinutesBetween(DateTime startUtc, DateTime endUtc)
    {
        startUtc = AsUtc(startUtc);
        endUtc = AsUtc(endUtc);
        if (endUtc <= startUtc) return 0;
        if (IsAlwaysOpen) return (int)(endUtc - startUtc).TotalMinutes;

        var start = TimeZoneInfo.ConvertTimeFromUtc(startUtc, _zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(endUtc, _zone);
        var total = TimeSpan.Zero;
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            if (!TryGetWindow(date, out var open, out var close)) continue;
            var from = start > open ? start : open;
            var to = end < close ? end : close;
            if (to > from) total += to - from;
        }
        return (int)total.TotalMinutes;
    }

    private bool TryGetWindow(DateTime localDate, out DateTime open, out DateTime close)
    {
        open = close = default;
        if (_holidays.Contains(localDate) || _yearlyHolidays.Contains((localDate.Month, localDate.Day))) return false;
        if (!_hours.TryGetValue(localDate.DayOfWeek, out var hours)) return false;
        open = localDate + hours.Start;
        close = localDate + hours.End;
        return true;
    }

    private DateTime ToUtc(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A local time skipped by a daylight-saving change does not exist; move past the gap
        while (_zone.IsInvalidTime(local)) local = local.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(local, _zone);
    }

    // Values read from the database are UTC; values built in code without a kind are treated as UTC too
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
