using ItsTool.Application.Constants;
using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Risks;
using ItsTool.Infrastructure.Services;
using ItsTool.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ItsTool.UnitTests.Services;

public class RiskServiceTests : TestBase
{
    private User _manager = null!, _approver = null!, _owner = null!, _other = null!;
    private readonly RiskService _service;

    public RiskServiceTests()
    {
        _manager = new User { Username = "manager", Email = "m@x", FirstName = "Mona" };
        _approver = new User { Username = "approver", Email = "a@x", FirstName = "Adel" };
        _owner = new User { Username = "owner", Email = "o@x", FirstName = "Omar" };
        _other = new User { Username = "other", Email = "x@x", FirstName = "Xena" };
        _context.Users.AddRange(_manager, _approver, _owner, _other);
        _context.SaveChanges();

        // The holders the reminders go to are read from the database, as in production
        var manage = new Permission { Key = PermissionConstants.RiskManage, Name = PermissionConstants.RiskManage };
        var role = new Role { Name = "Security Team" };
        _context.AddRange(manage, role);
        _context.SaveChanges();
        _context.AddRange(new RolePermission { RoleId = role.Id, PermissionId = manage.Id }, new UserRole { UserId = _manager.Id, RoleId = role.Id });
        _context.SaveChanges();

        var permissions = new Mock<IPermissionCalculator>();
        permissions.Setup(p => p.CalculateEffectivePermissionsAsync(It.IsAny<int>()))
            .ReturnsAsync((int userId) =>
                userId == _manager.Id ? new HashSet<string> { PermissionConstants.RiskManage } :
                userId == _approver.Id ? new HashSet<string> { PermissionConstants.RiskApprove } :
                new HashSet<string>());
        _service = new RiskService(_context, permissions.Object);
    }

    private static SaveRiskDto Form(string title = "Single point of failure", int c = 3, int i = 5, int a = 5, int likelihood = 4,
        int? owner = null, DateOnly? target = null, string? status = null, IReadOnlyList<string>? annex = null, string? treatment = null) =>
        new(title, "Outage", "One database server", "Nightly backup", c, i, a, likelihood, treatment, "Cluster the database", annex, owner, target, status);

    private Task<RiskDetailDto> CreateAsync(SaveRiskDto? form = null) => _service.CreateAsync(_manager.Id, form ?? Form());

    private Task<Risk> LoadAsync(int id) => _context.Risks.AsNoTracking().SingleAsync(r => r.Id == id);

    private Task<List<RiskHistory>> HistoryAsync(int id) => _context.RiskHistory.Where(h => h.RiskId == id).OrderBy(h => h.Id).ToListAsync();

    [Fact]
    public async Task A_risk_is_scored_from_its_ratings_and_numbered()
    {
        var first = await CreateAsync();
        var second = await CreateAsync(Form("Lost laptop", 2, 2, 2, 2, annex: new[] { "8.1", " 5.10 ", "8.1" }));

        Assert.Equal((1, 17.33, "High"), (first.Number, first.Score, first.Level));
        Assert.Equal((2, 4.0, "Low"), (second.Number, second.Score, second.Level));
        Assert.Equal(new[] { "5.10", "8.1" }, second.AnnexAControls);
        Assert.Equal("Open", first.Status);
        Assert.Equal(RiskActions.Created, Assert.Single(first.History).Kind);
        Assert.Equal("R-007", RiskService.Reference(7));
    }

    [Theory]
    [InlineData(4.0, "Low")]
    [InlineData(4.33, "Medium")]
    [InlineData(12.0, "Medium")]
    [InlineData(12.67, "High")]
    public void Fractional_scores_fall_in_a_band(double score, string level) => Assert.Equal(level, RiskScore.Level(score));

    [Fact]
    public async Task Only_the_security_team_keeps_the_register()
    {
        var risk = await CreateAsync(Form(owner: _owner.Id));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateAsync(_approver.Id, Form()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.UpdateAsync(_owner.Id, risk.Id, Form("Renamed")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.MarkReviewedAsync(_approver.Id, new[] { risk.Id }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateAsync(_other.Id, risk.Id, Form("Renamed")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetAsync(_other.Id, risk.Id));
    }

    [Fact]
    public async Task An_owner_sees_only_their_own_risks_and_approvers_see_all()
    {
        var owned = await CreateAsync(Form("Owned", owner: _owner.Id));
        var notOwned = await CreateAsync(Form("Someone else's"));

        var mine = await _service.ListAsync(_owner.Id);
        Assert.Equal(new[] { owned.Id }, mine.Risks.Select(r => r.Id));
        Assert.False(mine.CanManage || mine.CanApprove);
        Assert.False((await _service.GetAsync(_owner.Id, owned.Id)).CanManage);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetAsync(_owner.Id, notOwned.Id));

        Assert.Empty((await _service.ListAsync(_other.Id)).Risks);
        var all = await _service.ListAsync(_approver.Id);
        Assert.Equal(2, all.Risks.Count);
        Assert.True(all.CanApprove);
        Assert.Equal("Omar", all.Risks[0].OwnerName);
    }

    [Fact]
    public async Task Approval_is_one_click_and_a_change_to_the_plan_clears_it()
    {
        var risk = await CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ApproveAsync(_manager.Id, new[] { risk.Id }));

        Assert.Equal(1, await _service.ApproveAsync(_approver.Id, new[] { risk.Id, 999 }));
        Assert.Equal(0, await _service.ApproveAsync(_approver.Id, new[] { risk.Id }));
        Assert.Equal(_approver.Id, (await LoadAsync(risk.Id)).ApprovedByUserId);

        // Wording and status are not the plan
        await _service.UpdateAsync(_manager.Id, risk.Id, Form("Single point of failure of the database", status: "Treating"));
        Assert.NotNull((await LoadAsync(risk.Id)).ApprovedAt);

        await _service.UpdateAsync(_manager.Id, risk.Id, Form("Single point of failure of the database", status: "Treating", target: new DateOnly(2026, 12, 31)));
        Assert.Null((await LoadAsync(risk.Id)).ApprovedAt);
        Assert.Contains(await HistoryAsync(risk.Id), h => h.Kind == RiskActions.ApprovalCleared);
        Assert.Contains(await HistoryAsync(risk.Id), h => h.Kind == RiskActions.Approved && h.Note == "C3 I5 A5 L4 = 17.33 (High)");
    }

    [Fact]
    public async Task Each_change_is_recorded_with_its_old_and_new_value()
    {
        var risk = await CreateAsync();

        await _service.UpdateAsync(_manager.Id, risk.Id, Form(owner: _owner.Id, status: "Treating", target: new DateOnly(2026, 6, 30)));

        var changes = (await HistoryAsync(risk.Id)).Where(h => h.Kind == RiskActions.Updated).Select(h => (h.Field, h.OldValue, h.NewValue)).ToList();
        Assert.Equal(new (string?, string?, string?)[] { ("Owner", null, _owner.Id.ToString()), ("TargetDate", null, "2026-06-30"), ("Status", "Open", "Treating") }, changes);
        Assert.Equal("Omar", (await _service.GetAsync(_manager.Id, risk.Id)).UserNames[_owner.Id]);
    }

    [Fact]
    public async Task Invalid_input_is_refused()
    {
        var inactive = new User { Username = "gone", Email = "g@x", FirstName = "G", IsActive = false };
        _context.Users.Add(inactive);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(_manager.Id, Form(c: 6)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(_manager.Id, Form(title: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(_manager.Id, Form(annex: new[] { "9.9" })));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(_manager.Id, Form(owner: inactive.Id)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(_manager.Id, Form(treatment: "Ignore")));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(_manager.Id, Form() with { ResidualConfidentiality = 2, ResidualIntegrity = 2 }));
        Assert.Empty(_context.Risks);
    }

    [Fact]
    public async Task A_review_records_the_scores_of_the_day_and_old_risks_show_as_due()
    {
        var risk = await CreateAsync(Form() with { ResidualConfidentiality = 3, ResidualIntegrity = 4, ResidualAvailability = 5, ResidualLikelihood = 1 });
        var old = await CreateAsync(Form("Old risk"));
        var stored = await _context.Risks.SingleAsync(r => r.Id == old.Id);
        stored.CreatedAt = DateTime.UtcNow.AddDays(-400);
        await _context.SaveChangesAsync();

        Assert.Equal(new[] { false, true }, (await _service.ListAsync(_manager.Id)).Risks.Select(r => r.ReviewDue));
        Assert.Equal(1, await _service.MarkReviewedAsync(_manager.Id, new[] { old.Id }));

        Assert.All((await _service.ListAsync(_manager.Id)).Risks, r => Assert.False(r.ReviewDue));
        Assert.Equal("C3 I5 A5 L4 = 17.33 (High)", (await HistoryAsync(old.Id)).Single(h => h.Kind == RiskActions.Reviewed).Note);
        Assert.Equal((4.0, "Low"), (risk.ResidualScore!.Value, risk.ResidualLevel));
    }

    private static MemoryStream ExcelRegister() => TestWorkbook.Build(TestWorkbook.RegisterHeadings
        .Concat(TestWorkbook.RegisterRow(5, "1", "Single point of failure", "3", "5", "5", "4", annex: "A5.30", owner: "Head of IT Section", date: "Q4 2024", status: "Closed", residual: "3,4,5,1"))
        .Concat(TestWorkbook.RegisterRow(6, "2", "No offline backup", "3", "3", "4", "4", annex: "A.5.23, A.8.13", owner: "Cybersecurity Team Lead", date: "Q432025", status: "Planned"))
        .Concat(TestWorkbook.RegisterRow(7, "3", "Not rated", "", "", "", "")));

    [Fact]
    public async Task The_excel_register_is_previewed_then_imported_once()
    {
        using (var file = ExcelRegister())
        {
            var preview = await _service.PreviewImportAsync(_manager.Id, file);
            Assert.Equal(new[] { "Cybersecurity Team Lead", "Head of IT Section" }, preview.Owners);
            Assert.Equal(new string?[] { null, null, RiskService.SkipNoRatings }, preview.Rows.Select(r => r.Skip));
            Assert.Equal(new[] { "date:Q432025" }, preview.Rows[1].Warnings);
            Assert.Empty(_context.Risks);
        }

        using (var file = ExcelRegister())
        {
            var result = await _service.ImportAsync(_manager.Id, file, new Dictionary<string, int> { ["head of it section"] = _owner.Id });
            Assert.Equal((2, 1), (result.Imported, result.Skipped));
        }

        var risks = await _context.Risks.OrderBy(r => r.Number).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, risks.Select(r => r.Number));
        Assert.Equal((_owner.Id, RiskStatus.Done, new DateOnly(2024, 12, 31), "5.30"), (risks[0].OwnerUserId!.Value, risks[0].Status, risks[0].TargetDate!.Value, risks[0].AnnexAControls));
        Assert.Equal((null, RiskStatus.Treating, (DateOnly?)null, "5.23,8.13"), (risks[1].OwnerUserId, risks[1].Status, risks[1].TargetDate, risks[1].AnnexAControls));
        Assert.Equal(4.0, RiskScore.ResidualOf(risks[0]));
        Assert.Equal("Excel row 5", (await HistoryAsync(risks[0].Id)).Single().Note);

        // Importing the same file again adds nothing
        using (var again = ExcelRegister())
        {
            var preview = await _service.PreviewImportAsync(_manager.Id, again);
            Assert.Equal(RiskService.SkipAlreadyInRegister, preview.Rows[0].Skip);
            again.Position = 0;
            Assert.Equal(0, (await _service.ImportAsync(_manager.Id, again, new Dictionary<string, int>())).Imported);
        }
    }

    [Fact]
    public async Task Only_the_security_team_imports_and_owners_must_be_active_users()
    {
        using var file = ExcelRegister();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.PreviewImportAsync(_approver.Id, file));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ImportAsync(_manager.Id, file, new Dictionary<string, int> { ["Head of IT Section"] = 999 }));
        Assert.Empty(_context.Risks);
    }

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessCalendar.ResolveTimeZone(null)));

    [Fact]
    public async Task Owners_are_reminded_once_before_the_target_date_and_once_when_it_passes()
    {
        var dueSoon = await CreateAsync(Form("Due soon", owner: _owner.Id, target: Today.AddDays(7)));
        var overdue = await CreateAsync(Form("Overdue", owner: _owner.Id, target: Today.AddDays(-1)));
        await CreateAsync(Form("Later", owner: _owner.Id, target: Today.AddDays(60)));
        await CreateAsync(Form("Finished", owner: _owner.Id, target: Today.AddDays(-30), status: "Done"));
        await CreateAsync(Form("Nobody's", target: Today.AddDays(3)));

        Assert.Equal(4, await _service.SendRemindersAsync(DateTime.UtcNow));
        Assert.Equal(0, await _service.SendRemindersAsync(DateTime.UtcNow));

        var sent = await _context.Notifications.OrderBy(n => n.Id).Select(n => new { n.UserId, n.Type, n.EntityId }).ToListAsync();
        Assert.Contains(sent, n => n.UserId == _owner.Id && n.Type == "risk.due" && n.EntityId == dueSoon.Id);
        Assert.Contains(sent, n => n.UserId == _owner.Id && n.Type == "risk.overdue" && n.EntityId == overdue.Id);
        Assert.Contains(sent, n => n.UserId == _manager.Id && n.Type == "risk.overdue" && n.EntityId == overdue.Id);
        // A risk with no owner goes to the security team
        Assert.Contains(sent, n => n.UserId == _manager.Id && n.Type == "risk.due" && n.EntityId != dueSoon.Id);

        // A new target date earns a new reminder
        await _service.UpdateAsync(_manager.Id, dueSoon.Id, Form("Due soon", owner: _owner.Id, target: Today.AddDays(10)));
        Assert.Equal(1, await _service.SendRemindersAsync(DateTime.UtcNow));
    }

    [Fact]
    public async Task The_security_team_is_reminded_monthly_while_the_yearly_review_is_due()
    {
        var risk = await CreateAsync();
        Assert.Equal(0, await _service.SendRemindersAsync(DateTime.UtcNow));

        var stored = await _context.Risks.SingleAsync(r => r.Id == risk.Id);
        stored.CreatedAt = DateTime.UtcNow.AddDays(-366);
        await _context.SaveChangesAsync();

        Assert.Equal(1, await _service.SendRemindersAsync(DateTime.UtcNow));
        Assert.Equal(0, await _service.SendRemindersAsync(DateTime.UtcNow.AddDays(1)));
        var reminder = await _context.Notifications.SingleAsync();
        Assert.Equal((_manager.Id, "risk.review", 0), (reminder.UserId, reminder.Type, reminder.EntityId));
    }
}
