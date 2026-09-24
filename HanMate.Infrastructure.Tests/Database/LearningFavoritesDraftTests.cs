using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class LearningFavoritesDraftTests
{
    [Theory]
    [InlineData("quantifiers", "一个 一只 一条 一本 一张 一支 一把 一件 一双 一杯 一碗 一辆")]
    [InlineData("length", "毫米 厘米 分米 米 千米 公里")]
    [InlineData("seasonings", "盐 糖 酱油 醋 食用油 香油 胡椒粉 花椒 八角 料酒 蚝油 辣椒酱")]
    [InlineData("kitchenware", "锅 平底锅 菜刀 砧板 锅铲 漏勺 碗 盘子 筷子 勺子 电饭锅 水壶")]
    [InlineData("household", "牙刷 牙膏 毛巾 肥皂 洗发水 沐浴露 卫生纸 纸巾 洗衣液 扫帚 拖把 垃圾桶")]
    public async Task CategoryWordsHaveOrderedAnnotatedSavedExamples(string category, string order)
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var rows = await new LearningCatalogStore(db).QueryAsync(new(ContentKind.Word, WordCategory: category));
        Assert.Equal(order.Split(' '), rows.Items.Select(r => r.Title));
        var audio = new ReadingAudioStore(db);
        foreach (var row in rows.Items)
        {
            var document = JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
            var head = document.TextUnits.Single(u => u.Role == TextUnitRole.Headword);
            var detail = HanMate.Infrastructure.Dictionary.DictionaryDetailProjection.Create(document, sourceOnly: true);
            Assert.NotEmpty(detail.Definitions);
            Assert.Equal(2, detail.Examples.Count);
            Assert.All(document.TextUnits.SelectMany(u => u.Tokens).Where(t => t.Kind == TokenKind.Hanzi), t => Assert.NotNull(t.Pinyin));
            if (category == "quantifiers")
                Assert.Equal(row.Title is "一个" or "一件" or "一辆" ? "yí" : "yì", head.Tokens[0].Pinyin!.Display);
            var examples = document.TextUnits.Where(u => u.Role == TextUnitRole.Example).ToArray();
            Assert.Equal(examples.Select(e => e.Text), detail.Examples.Select(e => e.Text));
            foreach (var example in examples)
            {
                Assert.Contains(head.Text, example.Text);
                Assert.False(string.IsNullOrWhiteSpace(example.Translations["en"]));
                Assert.False(string.IsNullOrWhiteSpace(example.Translations["ja"]));
                var step = Assert.Single((await audio.PlanAsync(new(document), new(example.Id, null), allowSpeechFallback: true)).Steps);
                Assert.Equal(example.Text, await audio.ReadSpeechAsync(step.AssetKey));
            }
            if (row.Title == "一条") Assert.Equal(new[] { "一条鱼", "水里有一条鱼。" }, examples.Select(e => e.Text));
        }
    }

    [Fact]
    public async Task NumberOrderUsesHeadwordsAndIsAppliedBeforePagination()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var catalog = new LearningCatalogStore(db);
        var expected = new[] { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十", "百", "千", "万" };
        var filter = new LearningFilter(ContentKind.Word, WordCategory: "numbers");
        Assert.Equal(expected, (await catalog.QueryAsync(filter)).Items.Select(x => x.Title));
        Assert.Equal(expected.Skip(5), (await catalog.QueryAsync(filter, 5)).Items.Select(x => x.Title));
        // A display title must not move the underlying number to a different teaching position.
        await Execute(db, "UPDATE content SET title='自定义标题' WHERE title='零'");
        Assert.Equal("零", JsonSerializer.Deserialize<ContentDocument>((await catalog.QueryAsync(filter)).Items[0].BodyJson, ContentJson.Options)!.TextUnits.Single(u => u.Role == TextUnitRole.Headword).Text);
        var first = await catalog.QueryAsync(new(ContentKind.Word));
        var rows = first.Items.ToList();
        for (var offset = 50; offset < first.Total; offset += 50)
            rows.AddRange((await catalog.QueryAsync(new(ContentKind.Word), offset)).Items);
        var all = rows.Select(x => x.Id).ToArray();
        Assert.Equal(first.Total, all.Length); Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Equal(all.Skip(17).Take(50), (await catalog.QueryAsync(new(ContentKind.Word), 17)).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task WordCategoriesCountDistinctVisibleWordsAndKeepCategoryWhenFiltering()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var catalog = new LearningCatalogStore(db);
        var counts = await catalog.GetWordCategoryCountsAsync();
        Assert.Equal(134, counts.Total);
        Assert.Equal(0, counts.Counts[WordCategories.Other]);
        foreach (var category in WordCategories.All)
        {
            var words = await catalog.QueryAsync(new(ContentKind.Word, WordCategory: category));
            Assert.True(words.Total > 0, category);
            Assert.Equal(counts.Counts[category], words.Total);
        }
        var weight = await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "weight"));
        Assert.Equal(new[] { "克", "两", "斤", "千克", "公斤", "吨" }, weight.Items.Select(x => x.Title));
        Assert.Empty((await catalog.QueryAsync(new(ContentKind.Word, Scenes: ["daily"], WordCategory: "weight"))).Items);
        Assert.Equal(6, (await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "weight"))).Total);
        var family = await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "family"));
        var address = await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "address"));
        Assert.Equal(family.Items.Single(x => x.Title == "妈妈").Id, address.Items.Single(x => x.Title == "妈妈").Id);
        Assert.Contains((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "greetings"))).Items, x => x.Title == "早上好");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => catalog.QueryAsync(new(ContentKind.Text, WordCategory: "weight")));

        // A withdrawn word disappears from both its category and the total count.
        await Execute(db, $"INSERT INTO resource_entry_override(resource_id,entry_id,removed,updated_at_utc) SELECT resource_id,entry_id,1,'2026-09-21T00:00:00Z' FROM resource_entry WHERE content_id='{weight.Items[0].Id}'");
        counts = await catalog.GetWordCategoryCountsAsync();
        Assert.Equal(133, counts.Total); Assert.Equal(5, counts.Counts["weight"]);
        await Execute(db, "UPDATE installed_resource SET enabled=0 WHERE resource_kind='learning'");
        Assert.Equal(0, (await catalog.GetWordCategoryCountsAsync()).Total);
        Assert.Empty((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "weight"))).Items);
    }

    [Fact]
    public async Task CategoryShareLookupDeduplicatesOverlapsAndStopsAfterPackageLimit()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var catalog = new LearningCatalogStore(db);
        var family = await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "family"));
        var address = await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "address"));
        var ids = await catalog.GetWordIdsForCategoriesAsync(["family", "address"]);
        Assert.Equal(family.Items.Select(x => x.Id).Concat(address.Items.Select(x => x.Id)).Distinct().Order(),
            ids.Order());
        Assert.Equal(101, (await catalog.GetWordIdsForCategoriesAsync(["all"])).Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            catalog.GetWordIdsForCategoriesAsync(["custom-" + Guid.NewGuid().ToString("N")]));
    }

    [Fact]
    public async Task UncategorizedWordsStayReachableAndRepeatedTagsDoNotInflateCounts()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var catalog = new LearningCatalogStore(db);
        var id = (await catalog.QueryAsync(new(ContentKind.Word, WordCategory: "weight"))).Items[0].Id;
        // Simulate older and imported scene metadata without changing document identities.
        await Execute(db, $"UPDATE content SET body_json=json_set(body_json,'$.scenes',json('[\"custom-topic\"]')) WHERE id='{id}'");
        Assert.Equal(id, Assert.Single((await catalog.QueryAsync(new(ContentKind.Word, WordCategory: WordCategories.Other))).Items).Id);
        Assert.Equal(id, Assert.Single(await catalog.GetWordIdsForCategoriesAsync([WordCategories.Other])));
        Assert.Equal(1, (await catalog.GetWordCategoryCountsAsync()).Counts[WordCategories.Other]);
        await Execute(db, $"UPDATE content SET body_json=json_set(body_json,'$.scenes',json('[\"word-weight\",\"word-weight\",\"word-numbers\"]')) WHERE id='{id}'");
        var counts = await catalog.GetWordCategoryCountsAsync();
        Assert.Equal(134, counts.Total); Assert.Equal(6, counts.Counts["weight"]); Assert.Equal(15, counts.Counts["numbers"]);
        Assert.Equal(0, counts.Counts[WordCategories.Other]);
    }

    [Fact]
    public async Task LearningCombinesDimensionsAndUsesOrWithinScenes()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var content = new SqliteContentDocumentStore(db, new()); var fixture = Fixture();
        foreach (var (title, difficulty, scenes) in new[] { ("A", Difficulty.Basic, new[] { "school" }), ("B", Difficulty.Basic, new[] { "home" }), ("C", Difficulty.Advanced, new[] { "home" }) })
            await content.SaveAsync(Clone(fixture) with { Title = title, Difficulty = difficulty, SchoolStage = SchoolStage.Primary, Grade = 2, Scenes = scenes }, 0);
        var catalog = new LearningCatalogStore(db);
        var result = await catalog.QueryAsync(new(ContentKind.Grammar, Difficulty.Basic, SchoolStage.Primary, 2, ["home", "school"]));
        Assert.Equal(2, result.Total); Assert.Equal(3, result.UnfilteredTotal); Assert.Equal(new[] { "A", "B" }, result.Items.Select(x => x.Title));
        Assert.Empty((await catalog.QueryAsync(new(ContentKind.Grammar, Difficulty.Basic, Grade: 3))).Items);
        Assert.Equal(new[] { "home", "school" }, await catalog.GetScenesAsync(ContentKind.Grammar));
        Assert.Equal(0, (await catalog.QueryAsync(new(ContentKind.Word))).UnfilteredTotal);
    }
    [Fact]
    public async Task LearningExcludesDictionaryDisabledRemovedAndRetainedAndPaginates()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var catalog = new LearningCatalogStore(db); var resources = new ResourceManagementStore(db);
        var initial = await catalog.QueryAsync(new(ContentKind.Word)); Assert.Equal(134, initial.Total);
        await Execute(db, "UPDATE installed_resource SET enabled=0 WHERE resource_kind='learning'");
        Assert.Empty((await catalog.QueryAsync(new(ContentKind.Word))).Items);
        await Execute(db, "UPDATE installed_resource SET enabled=1 WHERE resource_kind='learning'");
        var id = initial.Items[0].Id;
        await Execute(db, $"INSERT INTO resource_entry_override(resource_id,entry_id,removed,updated_at_utc) SELECT resource_id,entry_id,1,'2026-09-17T00:00:00Z' FROM resource_entry WHERE content_id='{id}'");
        Assert.Equal(133, (await catalog.QueryAsync(new(ContentKind.Word))).Total);
        var fixture = Fixture(); var content = new SqliteContentDocumentStore(db, new());
        for (var i = 0; i < 53; i++) await content.SaveAsync(Clone(fixture) with { Title = $"personal-{i:000}" }, 0);
        var first = await catalog.QueryAsync(new(ContentKind.Grammar, PersonalOnly: true)); var second = await catalog.QueryAsync(new(ContentKind.Grammar, PersonalOnly: true), 50);
        Assert.Equal(53, first.Total); Assert.Equal(50, first.Items.Count); Assert.Equal(3, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        await Execute(db, "UPDATE content SET origin='retained' WHERE title='personal-000'");
        Assert.Equal(52, (await catalog.QueryAsync(new(ContentKind.Grammar, PersonalOnly: true))).Total);
    }
    [Fact]
    public async Task FolderIdentityNormalizationOrderingAndProtectionPersist()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var store = new FavoriteStore(db);
        var system = Assert.Single(await store.GetFoldersAsync()); Assert.True(system.IsDefault);
        var first = await store.SaveFolderAsync("  Café  ", "notes"); var second = await store.SaveFolderAsync("Second", "");
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveFolderAsync("CAFE\u0301", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveFolderAsync(new string('好', 41), ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteFolderAsync(system));
        await store.MoveAsync(second, -1);
        var reopened = new FavoriteStore(new(dir.DatabasePath)); Assert.Equal(new[] { system.Id, second, first }, (await reopened.GetFoldersAsync()).Select(x => x.Id));
        var old = (await reopened.GetFoldersAsync()).Single(x => x.Id == first);
        await store.SaveFolderAsync("Renamed", "new", old);
        await Assert.ThrowsAsync<RevisionConflictException>(() => store.DeleteFolderAsync(old));
    }
    [Fact]
    public async Task MembershipPreservesUnchangedRowsAndInvalidFolderRollsBack()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var id = (await new ResourceStateStore(db).GetEntriesAsync(BundledResourceCatalog.LearningId))[0].ContentId;
        var store = new FavoriteStore(db); var folder = Assert.Single(await store.GetFoldersAsync()); var second = await store.SaveFolderAsync("Second", "");
        var membership = new FavoriteMembershipStore(db); var oldTime = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        await membership.SetMembershipAsync(id, [folder.Id], 1, oldTime);
        await store.SetSelectionAsync(id, [folder.Id, second], 2);
        Assert.Equal(oldTime.ToString("O"), await Scalar(db, $"SELECT added_at_utc FROM favorite_item WHERE folder_id='{folder.Id}'"));
        await Assert.ThrowsAsync<SqliteException>(() => store.SetSelectionAsync(id, [Guid.NewGuid()], 3));
        Assert.Equal(2, (await store.GetSelectionAsync(id)).FolderIds.Count); Assert.Equal(3, (await store.GetSelectionAsync(id)).Revision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => store.SetSelectionAsync(id, [], 2));
        var current = (await store.GetFoldersAsync()).Single(x => x.Id == second); await store.DeleteFolderAsync(current);
        Assert.Single((await store.GetSelectionAsync(id)).FolderIds); Assert.Equal(4, (await store.GetSelectionAsync(id)).Revision);
        Assert.NotNull(await new SqliteContentDocumentStore(db, new()).GetAsync(id));
    }
    [Fact]
    public async Task FavoritesOpenAllKindsAndRemainReadableWhileSourceDisabled()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db);
        var store = new FavoriteStore(db); var folder = Assert.Single(await store.GetFoldersAsync());
        var docs = await new ResourceStateStore(db).GetEntriesAsync(BundledResourceCatalog.LearningId);
        var content = new SqliteContentDocumentStore(db, new()); var kinds = new HashSet<ContentKind>();
        foreach (var row in docs)
        {
            var doc = (await content.GetAsync(row.ContentId))!.Document;
            if (kinds.Add(doc.Kind)) await store.SetSelectionAsync(doc.Id, [folder.Id], 1);
        }
        Assert.Equal(4, kinds.Count);
        await Execute(db, "UPDATE installed_resource SET enabled=0 WHERE resource_kind='learning'");
        Assert.All(await store.GetEntriesAsync(folder.Id), x => Assert.Equal("Disabled", x.SourceState));
        var resource = (await new ResourceManagementStore(db).GetAsync()).Single(x => x.State.ResourceId == BundledResourceCatalog.LearningId);
        var preview = await new ResourceManagementStore(db).PreviewUninstallAsync(resource); Assert.False(preview.CanUninstall); Assert.Equal(4, preview.Favorites);
    }
    [Fact]
    public async Task FolderDeleteFailureRollsBackMembershipRevisionAndRelationships()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db); var store = new FavoriteStore(db);
        var folderId = await store.SaveFolderAsync("Keep", ""); var folder = (await store.GetFoldersAsync()).Single(x => x.Id == folderId);
        var id = (await new ResourceStateStore(db).GetEntriesAsync(BundledResourceCatalog.DictionaryId))[0].ContentId;
        await store.SetSelectionAsync(id, [folderId], 1);
        await Execute(db, "CREATE TRIGGER prevent_delete BEFORE DELETE ON favorite_folder BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => store.DeleteFolderAsync(folder));
        Assert.Equal(2, (await store.GetSelectionAsync(id)).Revision); Assert.Single((await store.GetSelectionAsync(id)).FolderIds);
    }
    [Fact]
    public async Task DraftRoundtripConflictsCancellationAndDeletionLeaveContentUntouched()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await Seed(db); var store = new TextDraftStore(db);
        var body = new TextDraft("原文", "你好\r\n\r\ne\u0301 😀", ContentKind.Poem); var id = Guid.NewGuid(); var first = await store.SaveAsync(id, body, 0);
        var reopened = new TextDraftStore(new(dir.DatabasePath)); Assert.Equal(body, Assert.Single(await reopened.ListAsync()).Body);
        await store.SaveAsync(id, body with { Title = "updated" }, 1);
        await Assert.ThrowsAsync<RevisionConflictException>(() => store.SaveAsync(id, body, 1));
        await Assert.ThrowsAsync<RevisionConflictException>(() => store.DeleteAsync(first));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Guid.NewGuid(), body, 0, cancel.Token));
        Assert.Single(await store.ListAsync()); await store.DeleteAsync(Assert.Single(await store.ListAsync()));
        Assert.Empty(await store.ListAsync()); Assert.Equal(166L, await Scalar(db, "SELECT count(*) FROM content"));
    }
    [Fact]
    public async Task ReadingPreferencesPreserveOtherSettingsAndRejectInvalidScale()
    {
        using var dir = new TestDatabaseDirectory(); var settings = new VersionedLocalStateStore(new(dir.DatabasePath));
        await settings.SaveSettingsAsync("{\"uiLanguage\":\"ja\",\"unknownFutureKey\":42}", 0);
        var store = new ReadingPreferenceStore(settings); await store.SaveAsync(new(false, 1.75)); await store.SaveAsync(new(true, 2));
        Assert.Equal(new(true, 2), await new ReadingPreferenceStore(new(new(dir.DatabasePath))).GetAsync());
        using var json = JsonDocument.Parse((await settings.GetSettingsAsync())!.BodyJson); Assert.Equal("ja", json.RootElement.GetProperty("uiLanguage").GetString()); Assert.Equal(42, json.RootElement.GetProperty("unknownFutureKey").GetInt32());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(new(true, double.NaN)));
    }
    private static ContentDocument Fixture() => JsonSerializer.Deserialize<ContentDocument>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "grammar-content.json")), ContentJson.Options)!;
    private static ContentDocument Clone(ContentDocument source)
    {
        // Re-map all IDs because independent documents cannot share playback identities.
        var json = JsonSerializer.Serialize(source, ContentJson.Options);
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(json, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").Select(x => x.Value).Distinct().ToArray()) json = json.Replace(match, Guid.NewGuid().ToString());
        return JsonSerializer.Deserialize<ContentDocument>(json, ContentJson.Options)! with { Origin = ContentOrigin.Personal };
    }
    private static Task Seed(HanMateDatabase db) => new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
    private static async Task Execute(HanMateDatabase db, string sql) { using var c = await db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<object?> Scalar(HanMateDatabase db, string sql) { using var c = await db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return await cmd.ExecuteScalarAsync(); }
}
