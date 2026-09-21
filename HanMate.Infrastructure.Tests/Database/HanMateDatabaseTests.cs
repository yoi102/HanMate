using HanMate.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class HanMateDatabaseTests
{
    [Fact]
    public async Task Initialize_CreatesVersionTwoSchemaAndEnablesForeignKeysOnEveryConnection()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);

        await database.InitializeAsync();
        await using var connection = await database.OpenConnectionAsync();

        Assert.Equal(2L, await ScalarAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(21L, await ScalarAsync(connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection,
            "INSERT INTO favorite_item(folder_id,content_id,added_at_utc) VALUES('missing-folder','missing-content','2026-09-14T00:00:00Z');"));
        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Fact]
    public async Task ApplyInitialSchema_RollsBackEveryStatementWhenMigrationFails()
    {
        using var directory = new TestDatabaseDirectory();
        await using var connection = new SqliteConnection($"Data Source={directory.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;");

        await Assert.ThrowsAsync<SqliteException>(() => SqliteMigrationRunner.ApplyInitialSchemaAsync(
            connection,
            2,
            "CREATE TABLE migration_probe(id INTEGER PRIMARY KEY); INSERT INTO missing_table(id) VALUES(1);"));

        Assert.Equal(0L, await ScalarAsync(connection, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='migration_probe';"));
    }

    [Fact]
    public async Task Initialize_PreservesUnrecognizedVersionOneDatabase()
    {
        using var directory = new TestDatabaseDirectory();
        await using (var connection = new SqliteConnection($"Data Source={directory.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "CREATE TABLE legacy_marker(value TEXT NOT NULL); INSERT INTO legacy_marker(value) VALUES('keep'); PRAGMA user_version=1;");
        }

        await Assert.ThrowsAsync<UnknownDatabaseSchemaException>(
            () => new HanMateDatabase(directory.DatabasePath).InitializeAsync());

        await using var reopened = new SqliteConnection($"Data Source={directory.DatabasePath};Pooling=False");
        await reopened.OpenAsync();
        Assert.Equal("keep", await TextScalarAsync(reopened, "SELECT value FROM legacy_marker;"));
    }

    [Fact]
    public async Task Initialize_RejectsVersionTwoDatabaseMissingResourceTables()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        await database.InitializeAsync();
        await using (var connection = await database.OpenConnectionAsync())
            await ExecuteAsync(connection, "DROP TABLE resource_operation;");

        var exception = await Assert.ThrowsAsync<UnknownDatabaseSchemaException>(
            () => new HanMateDatabase(directory.DatabasePath).InitializeAsync());
        Assert.Contains("resource_operation", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> TextScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
