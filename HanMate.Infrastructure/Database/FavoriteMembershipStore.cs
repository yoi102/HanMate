using HanMate.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed class FavoriteMembershipStore(HanMateDatabase database)
{
    public async Task<long> SetMembershipAsync(
        Guid contentId,
        IReadOnlyCollection<Guid> finalFolderIds,
        long expectedMembershipRevision,
        DateTimeOffset addedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finalFolderIds);
        if (expectedMembershipRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedMembershipRevision));
        var folders = finalFolderIds.Distinct().ToArray();
        if (folders.Length != finalFolderIds.Count) throw new ArgumentException("Favorite folder IDs must be unique.", nameof(finalFolderIds));

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ContentTrashStore.EnsureActiveAsync(connection, transaction, contentId, cancellationToken);
        await using var revisionCommand = connection.CreateCommand();
        revisionCommand.Transaction = transaction;
        revisionCommand.CommandText = "UPDATE content SET membership_revision=membership_revision+1 WHERE id=$contentId AND membership_revision=$expected;";
        revisionCommand.Parameters.AddWithValue("$contentId", contentId.ToString("D"));
        revisionCommand.Parameters.AddWithValue("$expected", expectedMembershipRevision);
        if (await revisionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new RevisionConflictException("content-membership", contentId.ToString("D"), expectedMembershipRevision);

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM favorite_item WHERE content_id=$contentId AND folder_id NOT IN (SELECT value FROM json_each($folders));";
        deleteCommand.Parameters.AddWithValue("$contentId", contentId.ToString("D"));
        deleteCommand.Parameters.AddWithValue("$folders", System.Text.Json.JsonSerializer.Serialize(folders.Select(id => id.ToString("D"))));
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < folders.Length; index++)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO favorite_item(folder_id,content_id,sort_order,added_at_utc) VALUES($folderId,$contentId,(SELECT COALESCE(max(sort_order),-1)+1 FROM favorite_item WHERE folder_id=$folderId),$added) ON CONFLICT(folder_id,content_id) DO NOTHING;";
            insertCommand.Parameters.AddWithValue("$folderId", folders[index].ToString("D"));
            insertCommand.Parameters.AddWithValue("$contentId", contentId.ToString("D"));
            insertCommand.Parameters.AddWithValue("$added", addedAtUtc.ToUniversalTime().ToString("O"));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expectedMembershipRevision + 1;
    }
}
