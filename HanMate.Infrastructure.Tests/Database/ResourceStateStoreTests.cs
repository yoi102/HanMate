using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Database;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class ResourceStateStoreTests
{
    [Fact]
    public async Task ResourceLifecycle_PersistsOwnershipOverridesAvailabilityEpochAndReceipts()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        var document = LoadWordFixture();
        await SaveAsync(database, document);
        var store = new ResourceStateStore(database);
        var registration = CreateRegistration(document, ResourceKind.Dictionary);
        var entry = new ResourceEntryLink(document.Source.ResourceId!.Value, document.Source.EntryId!, document.Id);

        Assert.Equal(2, await store.RegisterAsync(registration, [entry], Operation(document.Source.ResourceId.Value, ResourceOperationType.Install, 1)));
        var installed = await store.GetAsync(document.Source.ResourceId.Value);
        Assert.NotNull(installed);
        Assert.True(installed.IsPresent);
        Assert.True(installed.IsEnabled);
        Assert.Equal(1, installed.RowRevision);
        Assert.Equal([document.Id], await store.GetQueryableContentIdsAsync(ResourceKind.Dictionary));

        Assert.Equal(3, await store.SetEntryRemovedAsync(
            registration.ResourceId, entry.EntryId, true, 1, DateTimeOffset.UtcNow,
            Operation(registration.ResourceId, ResourceOperationType.Remove, 2)));
        Assert.Empty(await store.GetQueryableContentIdsAsync(ResourceKind.Dictionary));

        Assert.Equal(4, await store.SetEntryRemovedAsync(
            registration.ResourceId, entry.EntryId, false, 2, DateTimeOffset.UtcNow,
            Operation(registration.ResourceId, ResourceOperationType.RestoreEntry, 3)));
        Assert.Equal([document.Id], await store.GetQueryableContentIdsAsync(ResourceKind.Dictionary));

        Assert.Equal(5, await store.SetEnabledAsync(
            registration.ResourceId, false, 3,
            Operation(registration.ResourceId, ResourceOperationType.Disable, 4)));
        Assert.Empty(await store.GetQueryableContentIdsAsync(ResourceKind.Dictionary));

        var enableOperation = Operation(registration.ResourceId, ResourceOperationType.Enable, 5);
        Assert.Equal(6, await store.SetEnabledAsync(registration.ResourceId, true, 4, enableOperation));
        Assert.Equal([document.Id], await store.GetQueryableContentIdsAsync(ResourceKind.Dictionary));

        var duplicateOperation = enableOperation with
        {
            OperationType = ResourceOperationType.Disable,
            ExpectedDataEpoch = 6
        };
        await Assert.ThrowsAnyAsync<Exception>(() => store.SetEnabledAsync(registration.ResourceId, false, 5, duplicateOperation));

        var restarted = new ResourceStateStore(new HanMateDatabase(directory.DatabasePath));
        var afterRollback = await restarted.GetAsync(registration.ResourceId);
        Assert.NotNull(afterRollback);
        Assert.True(afterRollback.IsEnabled);
        Assert.Equal(5, afterRollback.RowRevision);
        Assert.Single(await restarted.GetEntriesAsync(registration.ResourceId));

        await using var connection = await database.OpenConnectionAsync();
        Assert.Equal(6L, await ScalarAsync(connection, "SELECT data_epoch FROM app_state WHERE singleton=1;"));
        Assert.Equal(5L, await ScalarAsync(connection, "SELECT count(*) FROM resource_operation;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM resource_entry_override WHERE removed=0;"));
    }

    [Fact]
    public async Task Registration_RejectsDescriptorSourceAndDictionaryKindMismatchWithoutPartialRows()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        var grammar = LoadGrammarFixture();
        var resourceId = Guid.NewGuid();
        grammar = grammar with
        {
            Origin = ContentOrigin.Resource,
            Source = grammar.Source with { ResourceId = resourceId, ResourceVersion = "1.0.0", EntryId = "grammar-1" }
        };
        await SaveAsync(database, grammar);
        var registration = CreateRegistration(grammar, ResourceKind.Dictionary);
        var store = new ResourceStateStore(database);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RegisterAsync(
            registration,
            [new ResourceEntryLink(resourceId, "grammar-1", grammar.Id)],
            Operation(resourceId, ResourceOperationType.Install, 1)));

        Assert.Null(await store.GetAsync(resourceId));
        await using var connection = await database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT data_epoch FROM app_state WHERE singleton=1;"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM resource_operation;"));

        var word = LoadWordFixture();
        await SaveAsync(database, word);
        var wrongDescriptor = CreateRegistration(word, ResourceKind.Dictionary) with
        {
            Version = "1.0.1"
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.RegisterAsync(
            wrongDescriptor,
            [new ResourceEntryLink(word.Source.ResourceId!.Value, word.Source.EntryId!, word.Id)],
            Operation(word.Source.ResourceId.Value, ResourceOperationType.Install, 1)));
    }

    [Fact]
    public async Task SearchAliases_UseCompositeIdentityAndReplaceAtomically()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        var document = LoadWordFixture();
        await SaveAsync(database, document);
        var store = new SearchAliasStore(database);
        var first = Alias(0, "中国", "zhongguo");
        var second = Alias(1, "中國", "zhongguo");

        await store.ReplaceAsync(document.Id, [first, second]);
        Assert.Equal([0, 1], (await store.GetAsync(document.Id)).Select(alias => alias.AliasOrdinal));

        await Assert.ThrowsAsync<ArgumentException>(() => store.ReplaceAsync(document.Id, [first, first with { HanziKey = "重复" }]));
        Assert.Equal(2, (await store.GetAsync(document.Id)).Count);

        await Assert.ThrowsAnyAsync<JsonException>(() => store.ReplaceAsync(document.Id, [first with { SyllablesJson = "not-json" }]));
        Assert.Equal(2, (await store.GetAsync(document.Id)).Count);

        await store.ReplaceAsync(document.Id, [second with { AliasOrdinal = 2 }]);
        var replaced = await store.GetAsync(document.Id);
        Assert.Single(replaced);
        Assert.Equal(2, replaced[0].AliasOrdinal);
    }

    [Fact]
    public async Task RetainedContent_MetadataSurvivesRestartAndDoesNotEnterResourceQueries()
    {
        using var directory = new TestDatabaseDirectory();
        var database = new HanMateDatabase(directory.DatabasePath);
        var document = LoadWordFixture() with { Origin = ContentOrigin.Retained };
        await SaveAsync(database, document);
        var retainedAt = DateTimeOffset.UtcNow;
        await using (var connection = await database.OpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO retained_content(content_id,source_resource_id,source_entry_id,source_version,reason,retained_at_utc)
                VALUES($content,$resource,$entry,$version,'userAudio',$retained);
                """;
            command.Parameters.AddWithValue("$content", document.Id.ToString("D"));
            command.Parameters.AddWithValue("$resource", document.Source.ResourceId!.Value.ToString("D"));
            command.Parameters.AddWithValue("$entry", document.Source.EntryId!);
            command.Parameters.AddWithValue("$version", document.Source.ResourceVersion!);
            command.Parameters.AddWithValue("$retained", retainedAt.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        var restarted = new ResourceStateStore(new HanMateDatabase(directory.DatabasePath));
        var retained = await restarted.GetRetainedAsync(document.Id);
        Assert.NotNull(retained);
        Assert.Equal(RetainedReason.UserAudio, retained.Reason);
        Assert.Equal(document.Source.ResourceId, retained.SourceResourceId);
        Assert.Empty(await restarted.GetQueryableContentIdsAsync(ResourceKind.Dictionary));
        Assert.Empty(await restarted.GetQueryableContentIdsAsync(ResourceKind.Learning));
    }

    private static ResourceRegistration CreateRegistration(ContentDocument document, ResourceKind kind)
    {
        var resourceId = document.Source.ResourceId!.Value;
        var version = document.Source.ResourceVersion!;
        var entryId = document.Source.EntryId!;
        var kindValue = kind == ResourceKind.Dictionary ? "dictionary" : "learning";
        var descriptor = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            resourceId,
            version,
            resourceKind = kindValue,
            entries = new[] { new { entryId, contentId = document.Id } }
        });
        return new ResourceRegistration(
            resourceId, kind, version, descriptor, new string('a', 64), new string('b', 64),
            ResourceDistribution.External, IsPresent: true, IsEnabled: true, Priority: 200);
    }

    private static ResourceOperationCommit Operation(Guid resourceId, ResourceOperationType type, long epoch) =>
        new(Guid.NewGuid(), resourceId, type, epoch, DateTimeOffset.UtcNow, "{\"status\":\"committed\"}");

    private static SearchAliasValue Alias(int ordinal, string hanzi, string pinyin) =>
        new(ordinal, hanzi, pinyin, "zhong guo", "zhong1guo2", "zhong1 guo2", "[\"zhong1\",\"guo2\"]", null, 1);

    private static async Task SaveAsync(HanMateDatabase database, ContentDocument document) =>
        await new SqliteContentDocumentStore(database, new ContentDocumentValidator()).SaveAsync(document, 0);

    private static async Task<long> ScalarAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static ContentDocument LoadWordFixture() => LoadFixture("dictionary-entry.json");
    private static ContentDocument LoadGrammarFixture() => LoadFixture("grammar-content.json");

    private static ContentDocument LoadFixture(string name) =>
        JsonSerializer.Deserialize<ContentDocument>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)),
            ContentJson.Options)!;
}
