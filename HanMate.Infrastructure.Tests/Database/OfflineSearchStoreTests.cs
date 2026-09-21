using System.Diagnostics;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Resources;
using HanMate.Core.Search;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using Xunit.Abstractions;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class OfflineSearchStoreTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BundledSearchUsesOnlyConfirmedWordsAndKeepsSourcesSeparate()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var search = new OfflineSearchStore(db);
        Assert.Equal(2, (await search.GetSourcesAsync()).Count);
        var chinese = await search.SearchAsync("你好");
        Assert.NotEmpty(chinese.Items);
        // Bundled annotation is still needsReview. A working engine must not promote draft readings.
        Assert.Empty((await search.SearchAsync("nihao")).Items);
        foreach (var item in (await search.SearchAsync("中")).Items)
            Assert.Equal(ContentKind.Word, JsonSerializer.Deserialize<ContentDocument>(item.BodyJson, ContentJson.Options)!.Kind);
        Assert.Empty((await search.SearchAsync("静夜思")).Items);
        Assert.Empty((await search.SearchAsync("你好", Guid.NewGuid().ToString("D"))).Items);
        Assert.Empty((await search.SearchAsync("你%_\\' OR 1=1--")).Items);
        Assert.Empty((await search.SearchAsync("nǐ hào")).Items);
    }

    [Theory]
    [InlineData("你好", "nihao")]
    [InlineData("你好", "nǐ hǎo")]
    [InlineData("你好", "ni3hao3")]
    [InlineData("你好", "nǐhǎo")]
    [InlineData("女儿", "nv er")]
    [InlineData("女儿", "nu: er")]
    [InlineData("西安", "xi'an")]
    [InlineData("西安", "xian")]
    [InlineData("银行", "yin2hang2")]
    public async Task ConfirmedFixtureQueriesReturnExpectedWords(string headword, string query)
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        var collection = JsonSerializer.Deserialize<ContentCollection>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "contents.json")), ContentJson.Options)!;
        var word = collection.Contents.First(w => w.Kind == ContentKind.Word && w.TextUnits.Any(u => u.Role == TextUnitRole.Headword && u.Text == headword));
        word = word with { Origin = ContentOrigin.Personal, Source = word.Source with { ResourceId = null, ResourceVersion = null, EntryId = null },
            TextUnits = word.TextUnits.Select(u => u with { Tokens = u.Tokens.Select(t => t.Pinyin is null ? t : t with { ReviewState = AnnotationReviewState.Confirmed }).ToArray() }).ToArray() };
        await new SqliteContentDocumentStore(db, new()).SaveAsync(word, 0);
        var result = Assert.Single((await new OfflineSearchStore(db).SearchAsync(query)).Items);
        Assert.Equal(headword, result.Headword); Assert.Equal(SearchMatchTier.PinyinExact, result.MatchTier);
    }

    [Fact]
    public async Task SourceDisableRemoveAndMissingInvalidateCursorAndFilterSql()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var search = new OfflineSearchStore(db); var states = new ResourceStateStore(db);
        var page = await search.SearchAsync("你好");
        var source = await states.GetAsync(BundledResourceCatalog.LearningId);
        await states.SetEnabledAsync(source!.ResourceId, false, source.RowRevision,
            Operation(source.ResourceId, ResourceOperationType.Disable, page.Epoch));
        await Assert.ThrowsAsync<SearchCursorStaleException>(() => search.SearchAsync("你好", cursor: new("你好", null, page.Epoch, 0)));
        Assert.DoesNotContain((await search.SearchAsync("你好")).Items, i => i.SourceId == source.ResourceId.ToString("D"));
        Assert.DoesNotContain(await search.GetSourcesAsync(), i => i.Id == source.ResourceId.ToString("D"));
        var dictionary = await states.GetAsync(BundledResourceCatalog.DictionaryId);
        var entries = await states.GetEntriesAsync(dictionary!.ResourceId);
        var entry = entries[0];
        var content = await new SqliteContentDocumentStore(db, new()).GetAsync(entry.ContentId);
        var headword = content!.Document.TextUnits.Single(u => u.Role == TextUnitRole.Headword).Text;
        var exactHeadword = content.Document.TextUnits.Single(u => u.Role == TextUnitRole.Headword);
        Assert.Equal(entry.ContentId, (await search.FindEntryAsync(exactHeadword)).Document?.Id);
        Assert.Contains((await search.SearchAsync(headword)).Items, i => i.ContentId == entry.ContentId);
        await states.SetEntryRemovedAsync(dictionary.ResourceId, entry.EntryId, true, dictionary.RowRevision, DateTimeOffset.UtcNow,
            Operation(dictionary.ResourceId, ResourceOperationType.Remove, await search.GetEpochAsync()));
        Assert.DoesNotContain((await search.SearchAsync(headword)).Items, i => i.ContentId == entry.ContentId);
        Assert.NotEqual(entry.ContentId, (await search.FindEntryAsync(exactHeadword)).Document?.Id);
        await states.SetEntryRemovedAsync(dictionary.ResourceId, entry.EntryId, false, dictionary.RowRevision + 1, DateTimeOffset.UtcNow,
            Operation(dictionary.ResourceId, ResourceOperationType.RestoreEntry, await search.GetEpochAsync()));
        Assert.Contains((await search.SearchAsync(headword)).Items, i => i.ContentId == entry.ContentId);
        Assert.Equal(entry.ContentId, (await search.FindEntryAsync(exactHeadword)).Document?.Id);
        await Execute(db, "UPDATE installed_resource SET is_present=0,enabled=0; UPDATE app_state SET data_epoch=data_epoch+1;");
        Assert.Empty((await search.SearchAsync(headword)).Items);
        Assert.Null((await search.FindEntryAsync(exactHeadword)).Document);
        Assert.Empty(await search.GetSourcesAsync());
        var personal = ConfirmedPersonal(); await new SqliteContentDocumentStore(db, new()).SaveAsync(personal, 0);
        Assert.Single((await search.SearchAsync("zhongguo")).Items);
    }

    [Fact]
    public async Task PersonalSaveUpdatesIndexAndEpochAtomicallyAndUnconfirmedReadingsStayOut()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        var documents = new SqliteContentDocumentStore(db, new()); var search = new OfflineSearchStore(db);
        var word = ConfirmedPersonal(); await documents.SaveAsync(word, 0);
        var lookup = await search.FindEntryAsync(word.TextUnits.Single(u => u.Role == TextUnitRole.Headword));
        Assert.Equal(word.Id, lookup.Document?.Id); Assert.False(lookup.IsReadOnly);
        var before = await search.SearchAsync("zhong1guo2"); Assert.Single(before.Items);
        var unconfirmed = word with { TextUnits = word.TextUnits.Select(u => u with
            { Tokens = u.Tokens.Select(t => t.Kind == TokenKind.Hanzi ? t with { ReviewState = AnnotationReviewState.NeedsReview } : t).ToArray() }).ToArray() };
        await documents.SaveAsync(unconfirmed, 1);
        Assert.Empty((await search.SearchAsync("zhongguo")).Items);
        Assert.Single((await search.SearchAsync("中国")).Items);
        await Assert.ThrowsAsync<SearchCursorStaleException>(() => search.SearchAsync("zhong1guo2", cursor: new("zhong1guo2", null, before.Epoch, 0)));
        var epoch = await search.GetEpochAsync();
        await Execute(db, "CREATE TRIGGER fail_search BEFORE INSERT ON search_index BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => documents.SaveAsync(word, 2));
        Assert.Equal(epoch, await search.GetEpochAsync());
        Assert.Equal(2, (await documents.GetAsync(word.Id))!.RowRevision);
        Assert.Empty((await search.SearchAsync("zhongguo")).Items);
        await Execute(db, "DROP TRIGGER fail_search;");
        await documents.SaveAsync(word, 2);
        Assert.Single((await search.SearchAsync("zhongguo")).Items);
    }

    [Fact]
    public async Task RebuildRepairsMissingOrOldIndexWithoutPublishingPartialData()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        var word = ConfirmedPersonal(); await new SqliteContentDocumentStore(db, new()).SaveAsync(word, 0);
        await Execute(db, "DELETE FROM search_index;");
        Assert.Single((await new OfflineSearchStore(db).SearchAsync("zhongguo")).Items);
        await Execute(db, "UPDATE search_index SET index_version=99; CREATE TRIGGER fail_search BEFORE INSERT ON search_index BEGIN SELECT RAISE(ABORT,'injected'); END;");
        var store = new OfflineSearchStore(db); var epoch = await store.GetEpochAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => store.SearchAsync("中国"));
        Assert.Equal(epoch, await store.GetEpochAsync());
        Assert.Equal(99, (await new SearchAliasStore(db).GetAsync(word.Id)).Single().IndexVersion);
        await Execute(db, "DROP TRIGGER fail_search;");
        Assert.Single((await store.SearchAsync("zhongguo")).Items);
    }

    [Fact]
    public async Task AliasDedupSemanticRankAndPageCursorAreStable()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var word = ConfirmedPersonal(); await new SqliteContentDocumentStore(db, new()).SaveAsync(word, 0);
        var aliases = new SearchAliasStore(db); var alias = (await aliases.GetAsync(word.Id)).Single();
        await aliases.ReplaceAsync(word.Id, [alias with { HanziKey = "中国人民" }, alias with { AliasOrdinal = 1, HanziKey = "中国人" }]);
        var search = new OfflineSearchStore(db);
        var page = await search.SearchAsync("中国");
        Assert.Equal(SearchMatchTier.HanziExact, page.Items[0].MatchTier);
        Assert.NotEqual(word.Id, page.Items[0].ContentId); // personal priority 0 cannot outrank a resource exact match
        Assert.Equal("中国人", Assert.Single(page.Items, i => i.ContentId == word.Id).Headword);
        Assert.All((await search.SearchAsync("中国", "personal")).Items, i => Assert.Equal("personal", i.SourceId));
        await AddCorpus(db, word, 121);
        page = await search.SearchAsync("zhongguo");
        var ids = page.Items.Select(i => i.ContentId).ToList();
        Assert.Equal(50, page.Items.Count); Assert.True(page.Total > 100);
        var firstPageIds = ids.ToArray();
        while (page.Next is not null)
        { page = await search.SearchAsync("zhongguo", cursor: page.Next); ids.AddRange(page.Items.Select(i => i.ContentId)); }
        Assert.Equal(page.Total, ids.Count); Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(firstPageIds, (await search.SearchAsync("zhongguo")).Items.Select(i => i.ContentId));
        await Assert.ThrowsAsync<SearchCursorStaleException>(() => search.SearchAsync("中国", cursor: new("zhongguo", null, page.Epoch, 50)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search.SearchAsync("中", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task TenThousandWordSqliteBenchmark()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        var word = ConfirmedPersonal(); await new SqliteContentDocumentStore(db, new()).SaveAsync(word, 0);
        await AddCorpus(db, word, 9999);
        var search = new OfflineSearchStore(db);
        Assert.Equal(10000, (await search.SearchAsync("zhongguo")).Total);
        var queries = new[] { "中国", "中", "国", "zhongguo", "zhong1guo2", "zhōngguó", "zhong", "guo2", "nu:", "xi'an", "%" };
        var times = new List<double>();
        foreach (var query in queries)
        {
            await search.SearchAsync(query);
            var samples = new List<double>();
            for (var i = 0; i < 5; i++)
            { var timer = Stopwatch.StartNew(); await search.SearchAsync(query); timer.Stop(); samples.Add(timer.Elapsed.TotalMilliseconds); }
            times.AddRange(samples); output.WriteLine($"{query}: median={samples.Order().ElementAt(2):F2}ms max={samples.Max():F2}ms");
        }
        output.WriteLine($"10,000 synthetic confirmed word rows; 55 warm queries; P95={times.Order().ElementAt((int)Math.Ceiling(times.Count * .95)-1):F2}ms; max={times.Max():F2}ms");
        using var connection = await db.OpenConnectionAsync(); using var plan = connection.CreateCommand();
        plan.CommandText = "EXPLAIN QUERY PLAN SELECT content_id FROM search_index WHERE pinyin_joined LIKE '%zhong%';";
        using var reader = await plan.ExecuteReaderAsync(); while (await reader.ReadAsync()) output.WriteLine(reader.GetString(3));
    }

    private static ContentDocument ConfirmedPersonal()
    {
        var word = JsonSerializer.Deserialize<ContentDocument>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dictionary-entry.json")), ContentJson.Options)!;
        return word with { Id = Guid.NewGuid(), Origin = ContentOrigin.Personal,
            Source = word.Source with { ResourceId = null, ResourceVersion = null, EntryId = null },
            TextUnits = word.TextUnits.Select(u => u with { Tokens = u.Tokens.Select(t => t.Pinyin is null ? t : t with { ReviewState = AnnotationReviewState.Confirmed }).ToArray() }).ToArray() };
    }
    private static ResourceOperationCommit Operation(Guid id, ResourceOperationType type, long epoch) => new(Guid.NewGuid(), id, type, epoch, DateTimeOffset.UtcNow, "{}");
    private static async Task Execute(HanMateDatabase db, string sql)
    { using var connection = await db.OpenConnectionAsync(); using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task AddCorpus(HanMateDatabase db, ContentDocument template, int count)
    {
        // Deliberately duplicated headwords model a worst case: all candidates must be ranked before paging.
        // This is a disposable query corpus, not licensed or reviewed teaching data.
        using var connection = await db.OpenConnectionAsync(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE numbers(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM numbers WHERE n<$count)
            INSERT INTO content SELECT printf('00000000-0000-4000-8000-%012d',n),kind,origin,title,
                json_set(body_json,'$.id',printf('00000000-0000-4000-8000-%012d',n)),row_revision,membership_revision,
                content_revision,annotation_revision,metadata_revision,semantic_fingerprint,created_at_utc,updated_at_utc
            FROM numbers,content WHERE id=$id;
            INSERT INTO search_index SELECT c.id,0,'中国','zhongguo','zhong guo','zhong1guo2','zhong1 guo2',
                '[{"base":"zhong","tone":1},{"base":"guo","tone":2}]',NULL,1
            FROM content c WHERE c.id LIKE '00000000-0000-4000-8000-%';
            UPDATE app_state SET data_epoch=data_epoch+1;
            """;
        command.Parameters.AddWithValue("$count", count); command.Parameters.AddWithValue("$id", template.Id.ToString("D"));
        await command.ExecuteNonQueryAsync(); transaction.Commit();
    }
}
