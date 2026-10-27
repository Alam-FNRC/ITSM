using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Data;

/// <summary>
/// SQL Server's datetime2 does not store DateTimeKind. Without the UTC converter, dates come back
/// as Unspecified, are serialized without "Z", and the browser shows them 4 hours off in the UAE.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class UtcDateTimeTests
{
    private readonly SqlServerFixture _fixture;

    public UtcDateTimeTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Dates_read_back_as_utc_including_nullable_columns()
    {
        var createdAt = new DateTime(2026, 10, 27, 7, 30, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddHours(2);
        var id = await SaveDepartmentAsync(createdAt, updatedAt);

        await using var context = _fixture.CreateContext();
        var saved = await context.Departments.SingleAsync(d => d.Id == id);

        Assert.Equal(DateTimeKind.Utc, saved.CreatedAt.Kind);
        Assert.Equal(createdAt, saved.CreatedAt);
        Assert.NotNull(saved.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, saved.UpdatedAt.Value.Kind);
        Assert.Equal(updatedAt, saved.UpdatedAt.Value);
    }

    [Fact]
    public async Task Local_times_are_converted_to_utc_when_saved()
    {
        var local = new DateTime(2026, 10, 27, 11, 30, 0, DateTimeKind.Local);
        var id = await SaveDepartmentAsync(local, updatedAt: null);

        await using var context = _fixture.CreateContext();
        var saved = await context.Departments.SingleAsync(d => d.Id == id);

        Assert.Equal(local.ToUniversalTime(), saved.CreatedAt);
    }

    private async Task<int> SaveDepartmentAsync(DateTime createdAt, DateTime? updatedAt)
    {
        await using var context = _fixture.CreateContext();
        var department = new Department { Name = TestData.Unique("Dept"), CreatedAt = createdAt, UpdatedAt = updatedAt };
        context.Departments.Add(department);
        await context.SaveChangesAsync();
        return department.Id;
    }
}
