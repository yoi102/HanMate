using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class VersionedStateAndImportTests
{
    [Fact]
    public async Task DraftAndSettings_UseOptimisticVersionsAndPersistAcrossRestart()
    {
        using var directory = new TestDatabaseDirectory();
        var draftId = Guid.NewGuid();
        var store = new VersionedLocalStateStore(new HanMateDatabase(directory.DatabasePath));

        Assert.Equal(1, (await store.SaveDraftAsync(draftId, null, "content", "{\"text\":\"你好\"}", 0, DateTimeOffset.UtcNow)).RowRevision);
        Assert.Equal(2, (await store.SaveDraftAsync(draftId, null, "content", "{\"text\":\"您好\"}", 1, DateTimeOffset.UtcNow)).RowRevision);
        await Assert.ThrowsAsync<RevisionConflictException>(() =>
            store.SaveDraftAsync(draftId, null, "content", "{}", 1, DateTimeOffset.UtcNow));

        Assert.Equal(1, (await store.SaveSettingsAsync("{\"language\":\"zh-Hans\"}", 0)).RowRevision);
        Assert.Equal(2, (await store.SaveSettingsAsync("{\"language\":\"ja\"}", 1)).RowRevision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => store.SaveSettingsAsync("{}", 1));

        var restarted = new VersionedLocalStateStore(new HanMateDatabase(directory.DatabasePath));
        Assert.Equal("您好", JsonDocument.Parse((await restarted.GetDraftAsync(draftId))!.BodyJson).RootElement.GetProperty("text").GetString());
        Assert.Equal("ja", JsonDocument.Parse((await restarted.GetSettingsAsync())!.BodyJson).RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task FavoriteMembership_ChangesMembershipRevisionWithoutChangingContentRevision()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        var document = LoadGrammarFixture();
        await new SqliteContentDocumentStore(database, new ContentDocumentValidator()).SaveAsync(document, 0);
        var folderA = Guid.NewGuid();
        var folderB = Guid.NewGuid();
        await using (var connection = await database.OpenConnectionAsync())
        {
            await InsertFolderAsync(connection, folderA, "a");
            await InsertFolderAsync(connection, folderB, "b");
        }

        var membership = new FavoriteMembershipStore(database);
        Assert.Equal(2, await membership.SetMembershipAsync(document.Id, [folderA, folderB], 1, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<RevisionConflictException>(() =>
            membership.SetMembershipAsync(document.Id, [folderA], 1, DateTimeOffset.UtcNow));

        await using var reopened = await database.OpenConnectionAsync();
        Assert.Equal(2L, await ScalarAsync(reopened, "SELECT membership_revision FROM content;"));
        Assert.Equal(1L, await ScalarAsync(reopened, "SELECT row_revision FROM content;"));
        Assert.Equal(2L, await ScalarAsync(reopened, "SELECT count(*) FROM favorite_item;"));
    }

    [Fact]
    public async Task ImportOperation_AllowsNewOperationForSamePackageAndRollsBackDuplicateOperationMutation()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        var store = new ImportOperationStore(database);
        var first = CreateCommit("operation-1", "package-1");
        await store.CommitAsync(first);
        await store.CommitAsync(CreateCommit("operation-2", "package-1"));

        Assert.True(await store.HasCommittedOperationAsync("operation-1"));
        Assert.True(await store.HasCommittedOperationAsync("operation-2"));

        await Assert.ThrowsAsync<SqliteException>(() => store.CommitAsync(first, async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE app_state SET data_epoch=2 WHERE singleton=1;";
            await command.ExecuteNonQueryAsync(token);
        }));

        await using var reopened = await database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(reopened, "SELECT data_epoch FROM app_state WHERE singleton=1;"));
        Assert.Equal(1L, await ScalarAsync(reopened, "SELECT count(*) FROM import_receipt;"));
        Assert.Equal(2L, await ScalarAsync(reopened, "SELECT count(*) FROM import_operation;"));
    }

    [Fact]
    public async Task ImportOperation_FailedDataMutationLeavesNoReceiptOrOperation()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        var store = new ImportOperationStore(database);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(
            CreateCommit("failed-operation", "failed-package"),
            (_, _, _) => throw new InvalidOperationException("injected failure")));

        Assert.False(await store.HasCommittedOperationAsync("failed-operation"));
        await using var connection = await database.OpenConnectionAsync();
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM import_receipt WHERE package_id='failed-package';"));
    }

    private static ImportOperationCommit CreateCommit(string operationId, string packageId) => new(
        operationId,
        packageId,
        new string('a', 64),
        new string('b', 64),
        "merge",
        1,
        DateTimeOffset.UtcNow,
        "{\"status\":\"accepted\"}",
        "{\"status\":\"committed\"}");

    private static async Task InsertFolderAsync(SqliteConnection connection, Guid id, string nameKey)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO favorite_folder(id,name,name_key) VALUES($id,$name,$key);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$name", nameKey);
        command.Parameters.AddWithValue("$key", nameKey);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static ContentDocument LoadGrammarFixture() =>
        JsonSerializer.Deserialize<ContentDocument>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "grammar-content.json")),
            ContentJson.Options)!;
}
