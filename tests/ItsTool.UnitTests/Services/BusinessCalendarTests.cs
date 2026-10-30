using ItsTool.Domain.Entities.SLA;
using ItsTool.Infrastructure.Services;

namespace ItsTool.UnitTests.Services;

public class BusinessCalendarTests
{
    // Monday to Friday, 07:30-15:30 UAE time (UTC+4), so 03:30-11:30 UTC
    private static BusinessCalendar Calendar(params Holiday[] holidays)
    {
        var hours = Enumerable.Range(1, 5)
            .Select(d => new BusinessHour { DayOfWeek = (DayOfWeek)d, StartTime = new TimeSpan(7, 30, 0), EndTime = new TimeSpan(15, 30, 0), IsWorkingDay = true })
            .Append(new BusinessHour { DayOfWeek = DayOfWeek.Saturday, IsWorkingDay = false })
            .Append(new BusinessHour { DayOfWeek = DayOfWeek.Sunday, IsWorkingDay = false });
        return new BusinessCalendar(hours, holidays, BusinessCalendar.ResolveTimeZone(null));
    }

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Business_hours_are_read_in_UAE_time()
    {
        // 04:00 UTC is 08:00 in the UAE, half an hour after opening
        Assert.Equal(Utc(11, 2, 5), Calendar().AddWorkingMinutes(Utc(11, 2, 4), 60));
    }

    [Fact]
    public void Time_before_opening_starts_counting_at_opening()
    {
        Assert.Equal(Utc(11, 2, 4), Calendar().AddWorkingMinutes(Utc(11, 2, 1), 30));
    }

    [Fact]
    public void Minutes_left_at_closing_carry_over_to_the_next_working_day()
    {
        // Monday 15:00 local: 30 minutes on Monday, 30 on Tuesday from 07:30
        Assert.Equal(Utc(11, 3, 4), Calendar().AddWorkingMinutes(Utc(11, 2, 11), 60));
    }

    [Fact]
    public void The_weekend_is_skipped()
    {
        // Friday 15:00 local to Monday 08:00 local
        Assert.Equal(Utc(11, 9, 4), Calendar().AddWorkingMinutes(Utc(11, 6, 11), 60));
    }

    [Fact]
    public void Holidays_are_skipped()
    {
        var tuesday = new Holiday { Name = "One-off", Date = new DateTime(2026, 11, 3) };
        Assert.Equal(Utc(11, 4, 4), Calendar(tuesday).AddWorkingMinutes(Utc(11, 2, 11), 60));
    }

    [Fact]
    public void Recurring_holidays_apply_every_year()
    {
        var nationalDay = new Holiday { Name = "National Day", Date = new DateTime(2020, 12, 2), IsRecurring = true };
        // Tuesday 1 Dec 15:00 local; Wednesday 2 Dec is the holiday, so the rest falls on Thursday
        Assert.Equal(Utc(12, 3, 4), Calendar(nationalDay).AddWorkingMinutes(Utc(12, 1, 11), 60));
    }

    [Fact]
    public void Deleted_or_inactive_entries_are_ignored()
    {
        var deleted = new Holiday { Name = "Removed", Date = new DateTime(2026, 11, 3), IsDeleted = true };
        Assert.Equal(Utc(11, 3, 4), Calendar(deleted).AddWorkingMinutes(Utc(11, 2, 11), 60));

        var inactiveHours = new[] { new BusinessHour { DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 0, 0), IsWorkingDay = true, IsActive = false } };
        Assert.True(new BusinessCalendar(inactiveHours, Array.Empty<Holiday>(), BusinessCalendar.ResolveTimeZone(null)).IsAlwaysOpen);
    }

    [Fact]
    public void A_calendar_without_working_days_counts_every_minute()
    {
        // The old deadline loop never ended when no working day was configured
        var calendar = new BusinessCalendar(Array.Empty<BusinessHour>(), Array.Empty<Holiday>(), BusinessCalendar.ResolveTimeZone(null));
        Assert.Equal(Utc(11, 7, 12), calendar.AddWorkingMinutes(Utc(11, 7, 10), 120));
        Assert.Equal(120, calendar.WorkingMinutesBetween(Utc(11, 7, 10), Utc(11, 7, 12)));
    }

    [Fact]
    public void Working_minutes_between_two_times_count_only_open_hours()
    {
        var calendar = Calendar();
        Assert.Equal(60, calendar.WorkingMinutesBetween(Utc(11, 2, 4), Utc(11, 2, 5)));
        // Friday 15:00 local to Monday 08:30 local: 30 + 60
        Assert.Equal(90, calendar.WorkingMinutesBetween(Utc(11, 6, 11), Utc(11, 9, 4, 30)));
        Assert.Equal(0, calendar.WorkingMinutesBetween(Utc(11, 7, 8), Utc(11, 8, 8)));
        Assert.Equal(0, calendar.WorkingMinutesBetween(Utc(11, 2, 5), Utc(11, 2, 4)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(45)]
    [InlineData(480)]
    [InlineData(2000)]
    public void Adding_and_measuring_agree(int minutes)
    {
        var calendar = Calendar();
        var start = Utc(11, 5, 9, 17);
        Assert.Equal(minutes, calendar.WorkingMinutesBetween(start, calendar.AddWorkingMinutes(start, minutes)));
    }

    [Fact]
    public void The_default_zone_is_UAE_time_and_unknown_zones_are_refused()
    {
        Assert.Equal(TimeSpan.FromHours(4), BusinessCalendar.ResolveTimeZone(null).GetUtcOffset(Utc(11, 2, 0)));
        Assert.Throws<InvalidOperationException>(() => BusinessCalendar.ResolveTimeZone("Not/AZone"));
    }
}
