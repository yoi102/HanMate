using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record ImportOperationCommit(
    string OperationId,
    string PackageId,
    string PackageFingerprint,
    string ArchiveSha256,
    string ImportMode,
    long ExpectedDataEpoch,
    DateTimeOffset CommittedAtUtc,
    string ReceiptResultJson,
    string OperationResultJson);

public sealed class ImportOperationStore(HanMateDatabase database)
{
    public async Task CommitAsync(
        ImportOperationCommit commit,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task>? applyDataChanges = null,
        CancellationToken cancellationToken = default)
    {
        Validate(commit);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var epochCommand = connection.CreateCommand())
        {
            epochCommand.Transaction = transaction;
            epochCommand.CommandText = "SELECT data_epoch FROM app_state WHERE singleton=1;";
            var actualEpoch = Convert.ToInt64(await epochCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            if (actualEpoch != commit.ExpectedDataEpoch)
                throw new InvalidOperationException($"Import plan epoch {commit.ExpectedDataEpoch} is stale; current epoch is {actualEpoch}.");
        }

        await using (var receiptCommand = connection.CreateCommand())
        {
            receiptCommand.Transaction = transaction;
            receiptCommand.CommandText = """
                INSERT INTO import_receipt(package_id,package_fingerprint,archive_sha256,committed_at_utc,result_json)
                VALUES($packageId,$fingerprint,$archive,$committed,$result)
                ON CONFLICT(package_id) DO NOTHING;
                """;
            AddCommonParameters(receiptCommand, commit);
            receiptCommand.Parameters.AddWithValue("$result", commit.ReceiptResultJson);
            await receiptCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var identityCommand = connection.CreateCommand())
        {
            identityCommand.Transaction = transaction;
            identityCommand.CommandText = "SELECT package_fingerprint,archive_sha256 FROM import_receipt WHERE package_id=$packageId;";
            identityCommand.Parameters.AddWithValue("$packageId", commit.PackageId);
            await using var reader = await identityCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                || !string.Equals(reader.GetString(0), commit.PackageFingerprint.ToLowerInvariant(), StringComparison.Ordinal)
                || !string.Equals(reader.GetString(1), commit.ArchiveSha256.ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidOperationException("The package ID is already associated with different bytes or semantics.");
        }

        if (applyDataChanges is not null)
            await applyDataChanges(connection, transaction, cancellationToken).ConfigureAwait(false);

        await using (var operationCommand = connection.CreateCommand())
        {
            operationCommand.Transaction = transaction;
            operationCommand.CommandText = """
                INSERT INTO import_operation(operation_id,package_id,import_mode,expected_data_epoch,committed_at_utc,result_json)
                VALUES($operationId,$packageId,$mode,$epoch,$committed,$result);
                """;
            AddCommonParameters(operationCommand, commit);
            operationCommand.Parameters.AddWithValue("$operationId", commit.OperationId);
            operationCommand.Parameters.AddWithValue("$mode", commit.ImportMode);
            operationCommand.Parameters.AddWithValue("$epoch", commit.ExpectedDataEpoch);
            operationCommand.Parameters.AddWithValue("$result", commit.OperationResultJson);
            await operationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasCommittedOperationAsync(string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM import_operation WHERE operation_id=$id);";
        command.Parameters.AddWithValue("$id", operationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static void Validate(ImportOperationCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.PackageId);
        if (!IsSha256(commit.PackageFingerprint)) throw new ArgumentException("Package fingerprint must be 64 hexadecimal characters.", nameof(commit));
        if (!IsSha256(commit.ArchiveSha256)) throw new ArgumentException("Archive SHA-256 must be 64 hexadecimal characters.", nameof(commit));
        if (commit.ImportMode is not ("merge" or "replace")) throw new ArgumentOutOfRangeException(nameof(commit));
        if (commit.ExpectedDataEpoch < 1) throw new ArgumentOutOfRangeException(nameof(commit));
        using var receipt = JsonDocument.Parse(commit.ReceiptResultJson);
        using var operation = JsonDocument.Parse(commit.OperationResultJson);
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void AddCommonParameters(SqliteCommand command, ImportOperationCommit commit)
    {
        command.Parameters.AddWithValue("$packageId", commit.PackageId);
        command.Parameters.AddWithValue("$fingerprint", commit.PackageFingerprint.ToLowerInvariant());
        command.Parameters.AddWithValue("$archive", commit.ArchiveSha256.ToLowerInvariant());
        command.Parameters.AddWithValue("$committed", commit.CommittedAtUtc.ToUniversalTime().ToString("O"));
    }
}
