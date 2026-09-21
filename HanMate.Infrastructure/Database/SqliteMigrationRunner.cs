using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public static class SqliteMigrationRunner
{
    public static async Task ApplyInitialSchemaAsync(
        SqliteConnection connection,
        int targetVersion,
        string schemaSql,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaSql);
        if (targetVersion < 1) throw new ArgumentOutOfRangeException(nameof(targetVersion));
        if (connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("The SQLite connection must be open.");

        await ExecuteAsync(connection, "BEGIN IMMEDIATE;", cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, schemaSql, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, $"PRAGMA user_version={targetVersion};", cancellationToken).ConfigureAwait(false);
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA foreign_key_check;";
                await using var reader = await check.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("The migrated database contains foreign key violations.");
            }
            await ExecuteAsync(connection, "COMMIT;", cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await ExecuteAsync(connection, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                // Preserve the original migration failure when SQLite has already rolled back.
            }

            throw;
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
