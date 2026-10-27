using ItsTool.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests;

/// <summary>
/// Creates a throwaway SQL Server database, applies the real migrations to it, and drops it afterwards.
/// Uses LocalDB by default. Set ITSM_TEST_SQL to a server connection string (no database name) to
/// run against another server, as CI does.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    public SqlServerFixture()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ITSM_TEST_SQL") ?? LocalDb)
        {
            InitialCatalog = $"FnrcItsm_Test_{Guid.NewGuid():N}"
        };
        ConnectionString = builder.ConnectionString;
    }

    public string ConnectionString { get; }

    public ItsToolDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ItsToolDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
