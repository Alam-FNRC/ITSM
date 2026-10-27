using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ItsTool.IntegrationTests.Data;

[Collection(SqlServerCollection.Name)]
public class MigrationTests
{
    private readonly SqlServerFixture _fixture;

    public MigrationTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migrations_apply_cleanly_to_sql_server()
    {
        await using var context = _fixture.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public void Model_has_no_changes_missing_from_the_migrations()
    {
        // Fails when an entity or configuration changed without a new migration.
        // Fix: dotnet ef migrations add <Name> --project src/ItsTool.Infrastructure --startup-project src/ItsTool.API
        using var context = _fixture.CreateContext();

        var snapshotModel = context.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.NotNull(snapshotModel);
        if (snapshotModel is IMutableModel mutableModel)
        {
            snapshotModel = mutableModel.FinalizeModel();
        }
        snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);

        var differences = context.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel.GetRelationalModel(),
            context.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.Empty(differences);
    }
}
