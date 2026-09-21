using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Tests.Database;

public sealed partial class BackupTests
{
    private static DictionaryBookmarkStore Bookmarks(Area area) => new(new(area.Db));
    private static DictionaryBookmark Bookmark(string title = "一个") => new(DictionaryBookmarkStore.Xinhua, Guid.NewGuid(), title);

    [Fact]
    public async Task DictionaryBookmarkPersistsIdempotentlyWithoutCopyingContentOrOverwritingSettings()
    {
        using var a = new Area();
        await new VersionedLocalStateStore(a.Db).SaveSettingsAsync("{\"uiLanguage\":\"ja\",\"custom\":42}", 0);
        var entry = Bookmark(); var store = Bookmarks(a);
        await store.SetAsync(entry, true); await store.SetAsync(entry, true);
        Assert.Equal(entry, Assert.Single(await Bookmarks(a).GetAsync()));
        Assert.Equal(0L, await a.Scalar("SELECT count(*) FROM content"));
        Assert.Equal("ja", await a.Scalar("SELECT json_extract(body_json,'$.uiLanguage') FROM user_settings"));
        Assert.Equal(42L, await a.Scalar("SELECT json_extract(body_json,'$.custom') FROM user_settings"));
        await store.SetAsync(entry, false); await store.SetAsync(entry, false);
        Assert.Empty(await Bookmarks(a).GetAsync());
        Assert.Equal(42L, await a.Scalar("SELECT json_extract(body_json,'$.custom') FROM user_settings"));
    }

    [Fact]
    public async Task DictionaryBookmarksRoundTripWithoutSettingsAndRemainReferencesAcrossTwoHops()
    {
        using var a = new Area(); using var b = new Area(); using var c = new Area();
        var entry = Bookmark(); var local = Bookmark("本地");
        await Bookmarks(a).SetAsync(entry, true);
        await new VersionedLocalStateStore(b.Db).SaveSettingsAsync("{\"uiLanguage\":\"ja\"}", 0);
        await Bookmarks(b).SetAsync(local, true);
        using var bytes = await a.Export();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Read, true))
        {
            using var reader = new StreamReader(zip.GetEntry("collections.json")!.Open());
            var collections = JsonNode.Parse(await reader.ReadToEndAsync())!;
            Assert.Equal(2, collections["schemaVersion"]!.GetValue<int>());
            var reference = Assert.Single(collections["dictionaryBookmarks"]!.AsArray())!.AsObject();
            Assert.Equal(3, reference.Count);
            Assert.Equal(entry.EntryId.ToString(), reference["entryId"]!.GetValue<string>());
        }
        bytes.Position = 0;
        var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes, false);
        Assert.Equal(1, plan.Favorites); Assert.Equal(0, plan.Added); await importer.CommitAsync(plan);
        bytes.Position = 0; await importer.CommitAsync(await importer.PlanAsync(bytes, false));
        Assert.Equal(2, (await Bookmarks(b).GetAsync()).Count);
        Assert.Equal("ja", await b.Scalar("SELECT json_extract(body_json,'$.uiLanguage') FROM user_settings"));
        Assert.Equal(0L, await b.Scalar("SELECT count(*) FROM content"));
        using var onward = await b.Export(); var final = new BackupImportStore(c.Db);
        await final.CommitAsync(await final.PlanAsync(onward, true));
        Assert.Equal(await Bookmarks(b).GetAsync(), await Bookmarks(c).GetAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DictionaryBookmarkReplacementClearsOldReferencesAndCapturesSafety(bool incomingReferences)
    {
        using var a = new Area(); using var b = new Area();
        var incoming = Bookmark(); var old = Bookmark("原收藏");
        if (incomingReferences) await Bookmarks(a).SetAsync(incoming, true);
        await Bookmarks(b).SetAsync(old, true); using var bytes = await a.Export();
        // A legacy collections v1 merge must preserve local bookmarks.
        var merge = new BackupImportStore(b.Db); await merge.CommitAsync(await merge.PlanAsync(bytes));
        Assert.Contains(old, await Bookmarks(b).GetAsync()); bytes.Position = 0;
        var replacement = new BackupReplacementStore(b.Db); var plan = await replacement.PlanAsync(bytes);
        Assert.Equal(incomingReferences ? 2 : 1, plan.DeletedFavorites);
        var safety = await replacement.PrepareSafetyAsync(plan);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(safety, "hanmate.db")};Pooling=False"))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT body_json FROM user_settings";
            Assert.Contains(old, DictionaryBookmarkStore.Read((string?)await command.ExecuteScalarAsync()));
        }
        await b.Execute("CREATE TRIGGER fail_bookmarks BEFORE INSERT ON import_operation BEGIN SELECT RAISE(ABORT,'rollback'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => replacement.CommitAsync(plan));
        Assert.Contains(old, await Bookmarks(b).GetAsync());
        await b.Execute("DROP TRIGGER fail_bookmarks"); await replacement.CommitAsync(plan);
        var restored = await Bookmarks(b).GetAsync();
        if (incomingReferences) Assert.Equal(incoming, Assert.Single(restored)); else Assert.Empty(restored);
    }

    [Fact]
    public async Task DictionaryBookmarkChangeInvalidatesImportPreview()
    {
        using var a = new Area(); using var b = new Area(); using var bytes = await a.Export();
        var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes);
        var entry = Bookmark(); await Bookmarks(b).SetAsync(entry, true);
        Assert.Equal("PLAN_STALE", (await Assert.ThrowsAsync<PackageException>(() => importer.CommitAsync(plan))).Code);
        Assert.Equal(entry, Assert.Single(await Bookmarks(b).GetAsync()));
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("provider")] [InlineData("emptyId")] [InlineData("v1")] [InlineData("blankTitle")]
    public async Task MalformedDictionaryBookmarksRejectedBeforeDestinationWrite(string fault)
    {
        using var a = new Area(); using var b = new Area(); await Bookmarks(a).SetAsync(Bookmark(), true);
        using var bytes = await a.Export(); using var malformed = Rewrite(bytes, files =>
        {
            var root = JsonNode.Parse(files["collections.json"])!; var entries = root["dictionaryBookmarks"]!.AsArray();
            if (fault == "duplicate") entries.Add(entries[0]!.DeepClone());
            if (fault == "provider") entries[0]!["provider"] = "unknown";
            if (fault == "emptyId") entries[0]!["entryId"] = Guid.Empty.ToString();
            if (fault == "v1") root["schemaVersion"] = 1;
            if (fault == "blankTitle") entries[0]!["title"] = " ";
            files["collections.json"] = JsonSerializer.SerializeToUtf8Bytes(root, ContentJson.Options);
        });
        await Assert.ThrowsAsync<PackageException>(() => new BackupImportStore(b.Db).PlanAsync(malformed));
        Assert.False(File.Exists(b.Db.DatabasePath));
    }
}
