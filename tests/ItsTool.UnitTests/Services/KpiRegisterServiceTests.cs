using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Kpis;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Infrastructure.Services;
using ItsTool.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ItsTool.UnitTests.Services;

public class KpiRegisterServiceTests : TestBase
{
    private readonly User _manager, _owner, _other;
    private readonly Mock<IKpiService> _tickets = new();
    private readonly KpiRegisterService _service;
    private readonly int _capabilityId;

    public KpiRegisterServiceTests()
    {
        _manager = new User { Username = "manager", Email = "m@x", FirstName = "Mona" };
        _owner = new User { Username = "owner", Email = "o@x", FirstName = "Omar" };
        _other = new User { Username = "other", Email = "x@x", FirstName = "Xena" };
        _context.Users.AddRange(_manager, _owner, _other);
        _context.SaveChanges();

        var permissions = new Mock<IPermissionCalculator>();
        permissions.Setup(p => p.CalculateEffectivePermissionsAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) => userId == _manager.Id ? new HashSet<string> { PermissionConstants.KpiManage } : new HashSet<string>());
        _service = new KpiRegisterService(_context, permissions.Object, tickets: _tickets.Object);
        _capabilityId = _service.SaveCapabilityAsync(_manager.Id, null, new SaveKpiCapabilityDto(4, "ISMS", "نظام إدارة أمن المعلومات")).Result.Id;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private SaveKpiDto Form(int number = 6, string nameEn = "Average fault resolution time", string? cycle = null, string? resultType = null,
        string? yearEnd = null, string? source = null, int? owner = null) =>
        new(number, _capabilityId, nameEn, "متوسط زمن حل الأعطال", Unit: "hour", ResultType: resultType, Direction: "Down", Cycle: cycle, YearEnd: yearEnd, Source: source, OwnerUserId: owner);

    private Task<KpiDetailDto> CreateAsync(SaveKpiDto? form = null) => _service.CreateAsync(_manager.Id, form ?? Form());

    [Fact]
    public async Task Only_kpi_manage_keeps_the_definitions_and_an_owner_sees_their_own()
    {
        var owned = await CreateAsync(Form(owner: _owner.Id));
        var other = await CreateAsync(Form(7, "Servers and data centres"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateAsync(_owner.Id, Form(8)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.UpdateAsync(_owner.Id, owned.Id, Form(owner: _owner.Id)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SaveTargetAsync(_owner.Id, owned.Id, 2026, new SaveKpiTargetDto(5)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ReportAsync(_owner.Id, 2023, 2026));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetAsync(_owner.Id, other.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SaveValueAsync(_other.Id, owned.Id, Today.Year, 1, new SaveKpiValueDto(3)));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(Form(6, "Same number")));

        var mine = await _service.ListAsync(_owner.Id, null);
        Assert.Equal(new[] { owned.Id }, mine.Kpis.Select(k => k.Id));
        Assert.False(mine.CanManage);
        Assert.True(mine.Kpis[0].CanEnter);
        Assert.Equal(2, (await _service.ListAsync(_manager.Id, null)).Kpis.Count);
        Assert.True(await _service.OwnsAnyAsync(_owner.Id));
        Assert.False(await _service.OwnsAnyAsync(_other.Id));
    }

    [Fact]
    public async Task The_owner_enters_values_per_period_and_the_year_figure_follows_the_rule()
    {
        var count = await CreateAsync(Form(cycle: "Quarterly", owner: _owner.Id));
        var rate = await CreateAsync(Form(9, "Policies reviewed on time", cycle: "HalfYearly", resultType: "Percentage", owner: _owner.Id));
        var year = Today.Year - 1; // every period of last year has started

        await _service.SaveValueAsync(_owner.Id, count.Id, year, 1, new SaveKpiValueDto(5, "ESS, RASD"));
        await _service.SaveValueAsync(_owner.Id, count.Id, year, 2, new SaveKpiValueDto(7));
        await _service.SaveValueAsync(_owner.Id, rate.Id, year, 1, new SaveKpiValueDto(90));
        await _service.SaveValueAsync(_owner.Id, rate.Id, year, 2, new SaveKpiValueDto(100));
        await _service.SaveTargetAsync(_manager.Id, count.Id, year, new SaveKpiTargetDto(6));

        var list = await _service.ListAsync(_owner.Id, year);
        var countRow = list.Kpis.Single(k => k.Id == count.Id);
        Assert.Equal((7m, 6m, 2, 4), (countRow.YearValue, countRow.Target, countRow.PeriodsEntered, countRow.PeriodsDue));
        Assert.Equal(95m, list.Kpis.Single(k => k.Id == rate.Id).YearValue);

        // Corrections overwrite, nulls clear, and a period that has not started or does not exist is refused
        await _service.SaveValueAsync(_owner.Id, count.Id, year, 2, new SaveKpiValueDto(8));
        var detail = await _service.GetAsync(_owner.Id, count.Id);
        Assert.Equal(8m, detail.Values.Single(v => v.Period == 2).Value);
        Assert.Equal("ESS, RASD", detail.Values.Single(v => v.Period == 1).Note);
        Assert.Equal(_owner.Id, detail.Values[0].EnteredByUserId);
        await _service.SaveValueAsync(_owner.Id, count.Id, year, 1, new SaveKpiValueDto(null));
        Assert.Single((await _service.GetAsync(_owner.Id, count.Id)).Values);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveValueAsync(_owner.Id, count.Id, year, 5, new SaveKpiValueDto(1)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveValueAsync(_owner.Id, count.Id, Today.Year + 1, 1, new SaveKpiValueDto(1)));

        var report = await _service.ReportAsync(_manager.Id, year - 1, year);
        var reported = report.Kpis.Single(k => k.Id == count.Id);
        Assert.Equal(8m, reported.Values[year]);
        Assert.Null(reported.Values[year - 1]);
        Assert.Equal(6m, reported.Targets[year]);
        Assert.Equal(1, reported.Entered[year]);
    }

    [Fact]
    public async Task The_ticket_figure_fills_periods_nobody_typed_and_a_typed_value_wins()
    {
        _tickets.Setup(t => t.GetReportAsync(It.IsAny<KpiFilterDto>()))
            .ReturnsAsync((KpiFilterDto f) => new KpiReportDto(f.From!.Value, f.To!.Value, DateTime.UtcNow,
                new KpiSummaryDto(0, 0, 0, 0, 0, 0, 0, null, 342, null, 0, 0, 0, null), new(), new KpiBacklogDto(0, 0, 0, 0, 0, 0), new()));
        var kpi = await CreateAsync(Form(source: "TicketResolutionTime", yearEnd: "Average", owner: _owner.Id));

        var detail = await _service.GetAsync(_owner.Id, kpi.Id);
        var computed = detail.Values.Where(v => v.Computed).ToList();
        Assert.Equal(new[] { Today.Year - 1, Today.Year }, computed.Select(v => v.Year));
        Assert.All(computed, v => Assert.Equal(5.7m, v.Value));
        Assert.Equal(5.7m, (await _service.ListAsync(_manager.Id, null)).Kpis.Single().YearValue);

        await _service.SaveValueAsync(_owner.Id, kpi.Id, Today.Year, 1, new SaveKpiValueDto(6.1m, "Checked against the vendor's report"));
        detail = await _service.GetAsync(_owner.Id, kpi.Id);
        Assert.Equal(6.1m, detail.Values.Single(v => v.Year == Today.Year).Value);
        Assert.False(detail.Values.Single(v => v.Year == Today.Year).Computed);
    }

    [Fact]
    public async Task Owners_are_reminded_once_about_a_period_left_empty()
    {
        var kpi = await CreateAsync(Form(cycle: "Monthly", owner: _owner.Id));
        await CreateAsync(Form(7, "Nobody owns this one"));
        await CreateAsync(Form(8, "From the tickets", source: "TicketResolutionTime", owner: _owner.Id));
        var now = DateTime.UtcNow;

        Assert.Equal(1, await _service.SendRemindersAsync(now));
        Assert.Equal(0, await _service.SendRemindersAsync(now.AddHours(6)));

        var reminder = Assert.Single(_context.Notifications);
        var (year, period) = KpiPeriods.LastCompleted(KpiCycle.Monthly, DateOnly.FromDateTime(now)).Value;
        Assert.Equal((_owner.Id, KpiRegisterService.DueNotification, kpi.Id), (reminder.UserId, reminder.Type, reminder.EntityId));
        Assert.Contains(KpiPeriods.Key(KpiCycle.Monthly, year, period), reminder.Body);
    }

    [Fact]
    public async Task The_sheet_is_imported_once_with_its_capabilities_and_yearly_values()
    {
        using var first = Sheet();
        var preview = await _service.PreviewImportAsync(_manager.Id, first);
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal("Average fault resolution time", preview.Rows[0].NameEn);
        Assert.All(preview.Rows, r => Assert.Null(r.Skip));

        first.Position = 0;
        Assert.Equal((2, 0), (await _service.ImportAsync(_manager.Id, first)) is var result ? (result.Imported, result.Skipped) : default);

        var kpis = await _context.KpiDefinitions.Include(k => k.Capability).Include(k => k.Values).OrderBy(k => k.Number).ToListAsync();
        Assert.Equal(new[] { 6, 7 }, kpis.Select(k => k.Number));
        Assert.Equal(("ISMS", 4), (kpis[0].Capability!.NameEn, kpis[0].Capability.Number)); // the capability made in the constructor is reused
        Assert.Equal((KpiResultType.Number, KpiDirection.Down, KpiCycle.Yearly, KpiYearEnd.Last, "hour"), (kpis[0].ResultType, kpis[0].Direction, kpis[0].Cycle, kpis[0].YearEnd, kpis[0].Unit));
        Assert.Equal(new[] { (2024, 20.4m), (2025, 5.7m) }, kpis[0].Values.OrderBy(v => v.Year).Select(v => (v.Year, v.Value)));
        // A capability the sheet names in Arabic only is created with that name on both sides
        Assert.Equal((5, "المرونة", "المرونة"), (kpis[1].Capability!.Number, kpis[1].Capability.NameAr, kpis[1].Capability.NameEn));
        Assert.Equal(KpiDirection.Monitor, kpis[1].Direction);

        using var again = Sheet();
        var second = await _service.PreviewImportAsync(_manager.Id, again);
        Assert.All(second.Rows, r => Assert.Equal(KpiRegisterService.SkipAlreadyInRegister, r.Skip));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.PreviewImportAsync(_other.Id, Sheet()));
    }

    private static MemoryStream Sheet() => TestWorkbook.Build(TestWorkbook.KpiHeadings
        .Concat(TestWorkbook.KpiRow(9, "4", "نظام إدارة أمن المعلومات وفق الأيزو 27001", "6", "متوسط زمن حل الأعطال", direction: "تنازلي ▼", unit: "ساعة", v2024: "20.399999999999999", v2025: "5.7"))
        .Concat(TestWorkbook.KpiRow(10, "5", "المرونة", "7", "عدد الخوادم", direction: "للرصد ◆", unit: "رقم", v2023: "8", v2026: "9"))
        .Concat(TestWorkbook.KpiRow(11, "", "", "", "", v2026: "1")), "Sheet1"); // a value with no KPI name is ignored
}
