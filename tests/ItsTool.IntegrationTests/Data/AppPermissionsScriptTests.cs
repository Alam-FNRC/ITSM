using System.Text.RegularExpressions;
using ItsTool.Domain.Common;
using ItsTool.Domain.Entities;
using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ItsTool.IntegrationTests.Data;

// On a server the app's database account has only the itsm_app role from deploy/sql/app-permissions.sql, which
// every release's 2-database-update.sql ends with (docs/deployment-staging.md)
[Collection(SqlServerCollection.Name)]
public class AppPermissionsScriptTests
{
    private const string Probe = "itsm_app_probe";
    private readonly SqlServerFixture _fixture;

    public AppPermissionsScriptTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task App_role_changes_data_but_not_history_or_tables()
    {
        await using var context = _fixture.CreateContext();
        var tables = context.Model.GetEntityTypes()
            .Where(e => e.GetTableName() != null && e.GetViewName() == null)
            .GroupBy(e => e.GetTableName()!)
            // The rule the app itself applies (SystemAuditInterceptor): these rows are added, never changed
            .ToDictionary(g => g.Key, g => g.Any(e => typeof(IAppendOnly).IsAssignableFrom(e.ClrType) ||
                                                      e.ClrType == typeof(SystemAuditLog) || e.ClrType == typeof(TicketHistory) || e.ClrType == typeof(AuthEvent)));

        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        // Each release runs it again
        await RunScriptAsync(connection);
        await RunScriptAsync(connection);
        await ExecuteAsync(connection, $"IF DATABASE_PRINCIPAL_ID('{Probe}') IS NULL CREATE USER [{Probe}] WITHOUT LOGIN; ALTER ROLE [itsm_app] ADD MEMBER [{Probe}];");

        Assert.Contains(tables, t => t.Value);
        var wrong = new List<string>();
        foreach (var (table, appendOnly) in tables)
        {
            foreach (var permission in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                var expected = !(appendOnly && permission is "UPDATE" or "DELETE");
                if (await HasPermissionAsync(connection, $"dbo.{table}", "OBJECT", permission) != expected)
                    wrong.Add($"{table} {permission}: expected {(expected ? "allowed" : "denied")}");
            }
        }
        Assert.Empty(wrong);

        Assert.True(await HasPermissionAsync(connection, "dbo.__EFMigrationsHistory", "OBJECT", "SELECT"));
        Assert.False(await HasPermissionAsync(connection, "dbo.__EFMigrationsHistory", "OBJECT", "INSERT"));
        Assert.False(await HasPermissionAsync(connection, connection.Database, "DATABASE", "CREATE TABLE"));
        Assert.False(await HasPermissionAsync(connection, "dbo", "SCHEMA", "ALTER"));
    }

    private static async Task RunScriptAsync(SqlConnection connection)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "deploy", "sql", "app-permissions.sql"));
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch)) await ExecuteAsync(connection, batch);
        }
    }

    private static async Task<bool> HasPermissionAsync(SqlConnection connection, string securable, string securableClass, string permission)
    {
        await using var command = new SqlCommand(
            $"EXECUTE AS USER = '{Probe}'; SELECT HAS_PERMS_BY_NAME(@securable, @class, @permission); REVERT;", connection);
        command.Parameters.AddWithValue("@securable", securable);
        command.Parameters.AddWithValue("@class", securableClass);
        command.Parameters.AddWithValue("@permission", permission);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ItsTool.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("ItsTool.sln not found above " + AppContext.BaseDirectory);
    }
}
