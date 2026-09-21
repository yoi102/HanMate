using System.Security.Cryptography;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Content;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed class SqliteContentDocumentStore(HanMateDatabase database, ContentDocumentValidator validator)
    : IContentDocumentStore
{
    public async Task<ContentDocumentSnapshot?> GetAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT body_json,row_revision,membership_revision FROM content WHERE id=$id;";
        command.Parameters.AddWithValue("$id", contentId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var document = JsonSerializer.Deserialize<ContentDocument>(reader.GetString(0), ContentJson.Options)
            ?? throw new JsonException("Persisted content document is null.");
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new ContentDocumentValidationException(validation.Errors);
        return new ContentDocumentSnapshot(document, reader.GetInt64(1), reader.GetInt64(2));
    }

    public async Task<ContentDocumentSnapshot> SaveAsync(
        ContentDocument document,
        long expectedRowRevision,
        CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await SaveInTransactionAsync(connection, transaction, document, expectedRowRevision, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    internal async Task<ContentDocumentSnapshot> SaveInTransactionAsync(SqliteConnection connection, SqliteTransaction transaction,
        ContentDocument document, long expectedRowRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await ContentTrashStore.EnsureActiveAsync(connection, transaction, document.Id, cancellationToken);
        if (expectedRowRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowRevision));
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new ContentDocumentValidationException(validation.Errors);

        var body = JsonSerializer.Serialize(document, ContentJson.Options);
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var nextRevision = expectedRowRevision + 1;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = expectedRowRevision == 0
            ? """
              INSERT INTO content(
                id,kind,origin,title,body_json,row_revision,membership_revision,
                content_revision,annotation_revision,metadata_revision,semantic_fingerprint,
                created_at_utc,updated_at_utc)
              VALUES($id,$kind,$origin,$title,$body,1,1,$contentRevision,$annotationRevision,
                $metadataRevision,$fingerprint,$created,$updated);
              """
            : """
              UPDATE content SET
                kind=$kind,origin=$origin,title=$title,body_json=$body,row_revision=row_revision+1,
                content_revision=$contentRevision,annotation_revision=$annotationRevision,
                metadata_revision=$metadataRevision,semantic_fingerprint=$fingerprint,
                created_at_utc=$created,updated_at_utc=$updated
              WHERE id=$id AND row_revision=$expectedRevision;
              """;
        command.Parameters.AddWithValue("$id", document.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", ToCamelCase(document.Kind));
        command.Parameters.AddWithValue("$origin", ToCamelCase(document.Origin));
        command.Parameters.AddWithValue("$title", document.Title);
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$contentRevision", document.ContentRevision);
        command.Parameters.AddWithValue("$annotationRevision", document.AnnotationRevision);
        command.Parameters.AddWithValue("$metadataRevision", document.MetadataRevision);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$created", document.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updated", document.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$expectedRevision", expectedRowRevision);

        long membershipRevision;
        try
        {
            var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (changed != 1)
                throw new RevisionConflictException("content", document.Id.ToString("D"), expectedRowRevision);
            await using var membershipCommand = connection.CreateCommand();
            membershipCommand.Transaction = transaction;
            membershipCommand.CommandText = "SELECT membership_revision FROM content WHERE id=$id;";
            membershipCommand.Parameters.AddWithValue("$id", document.Id.ToString("D"));
            membershipRevision = Convert.ToInt64(await membershipCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            await WordSearchIndex.ReplaceAsync(connection, transaction, document, cancellationToken).ConfigureAwait(false);
            membershipCommand.CommandText = """
                UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1 AND
                ($personal=1 OR $updated=1 OR EXISTS(SELECT 1 FROM resource_entry WHERE content_id=$id));
                """;
            membershipCommand.Parameters.AddWithValue("$personal", document.Origin == ContentOrigin.Personal ? 1 : 0);
            membershipCommand.Parameters.AddWithValue("$updated", expectedRowRevision > 0 ? 1 : 0);
            await membershipCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new RevisionConflictException("content", document.Id.ToString("D"), expectedRowRevision);
        }

        return new ContentDocumentSnapshot(document, nextRevision, membershipRevision);
    }

    private static string ToCamelCase<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
