using System.Reflection;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed class HanMateDatabase
{
    public const int CurrentSchemaVersion = 2;
    private const string InitialSchemaResourceName = "HanMate.Infrastructure.Database.Migrations.0002_initial_schema.sql";
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    public HanMateDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var version = await GetUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            switch (version)
            {
                case CurrentSchemaVersion:
                    await VerifyRequiredSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
                    break;
                case 0 when !await HasUserTablesAsync(connection, cancellationToken).ConfigureAwait(false):
                    await SqliteMigrationRunner.ApplyInitialSchemaAsync(
                        connection,
                        CurrentSchemaVersion,
                        ReadInitialSchema(),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case 0:
                    throw new UnknownDatabaseSchemaException("The database has tables but no recognized user_version.");
                case 1:
                    await LegacyDatabaseMigration.ApplyAsync(connection, DatabasePath, ReadInitialSchema(), cancellationToken).ConfigureAwait(false);
                    await VerifyRequiredSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new DatabaseMigrationRequiredException(version, CurrentSchemaVersion,
                        version > CurrentSchemaVersion
                            ? "The database was created by a newer HanMate version."
                            : "No migration path is registered for this database version.");
            }

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            command.CommandText = "PRAGMA foreign_keys;";
            var enabled = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (enabled != 1) throw new InvalidOperationException("SQLite foreign keys could not be enabled for this connection.");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string ReadInitialSchema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(InitialSchemaResourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{InitialSchemaResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<bool> HasUserTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%');";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task VerifyRequiredSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        string[] requiredTables =
        [
            "app_state", "content", "pinyin_item", "playback_target", "audio_asset", "audio_binding",
            "audio_preference", "favorite_folder", "favorite_item", "user_settings", "search_index",
            "import_receipt", "import_operation", "import_mapping", "draft", "file_lease",
            "installed_resource", "resource_entry", "resource_entry_override", "retained_content",
            "resource_operation"
        ];
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) actual.Add(reader.GetString(0));
        var missing = requiredTables.Where(table => !actual.Contains(table)).ToArray();
        if (missing.Length > 0)
            throw new UnknownDatabaseSchemaException($"Database v{CurrentSchemaVersion} is missing required tables: {string.Join(", ", missing)}.");
    }
}

public sealed class DatabaseMigrationRequiredException(int actualVersion, int supportedVersion, string message) : Exception(message)
{
    public int ActualVersion { get; } = actualVersion;
    public int SupportedVersion { get; } = supportedVersion;
}

public sealed class UnknownDatabaseSchemaException(string message) : Exception(message)
{
}
