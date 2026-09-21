using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Database;

public sealed partial class BackupTests
{
    [Fact]
    public async Task FourKindsFavoritesSettingsAndActualAudioRoundTripAndRepeat()
    {
        using var a = new Area(); using var b = new Area(); var docs = new List<ContentDocument>();
        foreach (var kind in Enum.GetValues<ContentKind>()) docs.Add(await a.Seed(kind));
        var track = await a.Record(docs[2]);
        var favorites = new FavoriteStore(a.Db); var folders = await favorites.GetFoldersAsync(); var named = await favorites.SaveFolderAsync("复习", "说明");
        await favorites.SetSelectionAsync(docs[2].Id, [folders[0].Id, named], (await favorites.GetSelectionAsync(docs[2].Id)).Revision);
        await new VersionedLocalStateStore(a.Db).SaveSettingsAsync("{\"uiLanguage\":\"ja\",\"reading\":{\"showPinyin\":false,\"scale\":1.75}}", 0);
        using var bytes = await a.Export(); var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes, true);
        Assert.Equal(4, plan.Added); Assert.Equal(1, plan.AudioAdded); Assert.Equal(2, plan.Favorites);
        var result = await importer.CommitAsync(plan); Assert.Equal(4, result.Added);
        Assert.Equal(2L, await b.Scalar("SELECT count(*) FROM favorite_folder")); Assert.Equal(2L, await b.Scalar("SELECT count(*) FROM favorite_item"));
        Assert.Equal("ja", await b.Scalar("SELECT json_extract(body_json,'$.uiLanguage') FROM user_settings"));
        Assert.Equal(new ReadingPreference(false, 1.75), await new ReadingPreferenceStore(new(b.Db)).GetAsync());
        Assert.Equal("user", await b.Scalar("SELECT source_role FROM audio_binding"));
        await using (var lease = await new LocalAudioStore(b.Db).OpenPlaybackAsync("track", track))
        { using var audio = new MemoryStream(); await lease.Stream.CopyToAsync(audio); Assert.Equal(Wave(), audio.ToArray()); }
        foreach (var d in docs)
        {
            var saved = (await new SqliteContentDocumentStore(b.Db, new()).GetAsync(d.Id))!.Document;
            Assert.Equal(JsonSerializer.Serialize(d, ContentJson.Options), JsonSerializer.Serialize(saved, ContentJson.Options));
        }
        await importer.CommitAsync(plan); Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM import_operation"));
        bytes.Position = 0; var repeat = await importer.PlanAsync(bytes); Assert.Equal(4, repeat.Reused); Assert.Equal(0, repeat.AudioAdded);
        await importer.CommitAsync(repeat); Assert.Equal(2L, await b.Scalar("SELECT count(*) FROM import_operation")); Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM audio_binding"));
    }
    [Fact]
    public async Task ConflictRemapsGrammarAudioFavoritesAndKeepsLocalSettingsAndNames()
    {
        using var a = new Area(); using var b = new Area(); var doc = await a.Seed(ContentKind.Grammar); await a.Record(doc);
        var sourceFolders = new FavoriteStore(a.Db); var folder = await sourceFolders.SaveFolderAsync("同名", "source");
        await sourceFolders.SetSelectionAsync(doc.Id, [folder], (await sourceFolders.GetSelectionAsync(doc.Id)).Revision);
        await new SqliteContentDocumentStore(b.Db, new()).SaveAsync(doc with { Title = "Local change" }, 0);
        await new FavoriteStore(b.Db).SaveFolderAsync("同名", "local"); await new VersionedLocalStateStore(b.Db).SaveSettingsAsync("{\"uiLanguage\":\"en\"}", 0);
        using var bytes = await a.Export(); var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes); Assert.Equal(1, plan.Conflicts);
        var restored = Assert.Single((await importer.CommitAsync(plan)).ContentIds); Assert.NotEqual(doc.Id, restored);
        Assert.Equal(restored.ToString(), await b.Scalar("SELECT content_id FROM favorite_item"));
        Assert.Equal(restored.ToString(), await b.Scalar("SELECT content_id FROM playback_target t JOIN audio_binding b ON b.target_id=t.id"));
        Assert.Equal("en", await b.Scalar("SELECT json_extract(body_json,'$.uiLanguage') FROM user_settings"));
        Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM favorite_folder WHERE name='同名 (导入)'"));
        var copied = (await new SqliteContentDocumentStore(b.Db, new()).GetAsync(restored))!.Document;
        Assert.All(copied.Grammar!.ExampleUnitIds, id => Assert.Contains(copied.TextUnits, u => u.Id == id));
        Assert.Empty(doc.TextUnits.Select(u => u.Id).Intersect(copied.TextUnits.Select(u => u.Id)));
        bytes.Position = 0; var repeat = await importer.PlanAsync(bytes); Assert.Equal(1, repeat.Reused); Assert.Equal(0, repeat.FoldersAdded); await importer.CommitAsync(repeat);
        Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM favorite_item"));
        await new ContentTrashStore(b.Db).MoveAsync(await new ContentTrashStore(b.Db).PreviewAsync(restored));
        bytes.Position = 0; var again = await importer.PlanAsync(bytes); Assert.Equal(1, again.Added); await importer.CommitAsync(again);
        Assert.Single(await new ContentTrashStore(b.Db).ListAsync()); Assert.Equal(3L, await b.Scalar("SELECT count(*) FROM content"));
    }
    [Theory]
    [InlineData("settings")][InlineData("folder")][InlineData("draft")]
    public async Task PreviewGuardRejectsLocalChangesEvenWithoutEpochAdvance(string change)
    {
        using var a = new Area(); using var b = new Area(); await a.Seed(); using var bytes = await a.Export();
        var store = new BackupImportStore(b.Db); var plan = await store.PlanAsync(bytes);
        var epoch = await b.Scalar("SELECT data_epoch FROM app_state");
        if (change == "settings") await new VersionedLocalStateStore(b.Db).SaveSettingsAsync("{\"uiLanguage\":\"ja\"}", 0);
        if (change == "folder") await new FavoriteStore(b.Db).SaveFolderAsync("New folder", "");
        if (change == "draft") await new TextDraftStore(b.Db).SaveAsync(Guid.NewGuid(), new("unsaved", "你好", ContentKind.Text), 0);
        Assert.Equal(epoch, await b.Scalar("SELECT data_epoch FROM app_state"));
        Assert.Equal("PLAN_STALE", (await Assert.ThrowsAsync<PackageException>(() => store.CommitAsync(plan))).Code);
        Assert.Equal(0L, await b.Scalar("SELECT count(*) FROM content"));
    }
    [Fact]
    public async Task FailureAfterFilesAndMetadataRollsBackAndSamePlanCanRetry()
    {
        using var a = new Area(); using var b = new Area(); var doc = await a.Seed(); await a.Record(doc); using var bytes = await a.Export();
        var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes, true);
        await b.Execute("CREATE TRIGGER reject_backup BEFORE INSERT ON import_operation BEGIN SELECT RAISE(ABORT,'test failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => importer.CommitAsync(plan));
        foreach (var table in new[] { "content", "audio_binding", "audio_asset", "import_receipt", "import_mapping", "user_settings", "favorite_item" }) Assert.Equal(0L, await b.Scalar("SELECT count(*) FROM " + table));
        Assert.Single(Directory.GetFiles(Path.Combine(b.Root, "audio/packages")));
        await b.Execute("DROP TRIGGER reject_backup"); await importer.CommitAsync(plan); Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM content"));
    }
    [Fact]
    public async Task SnapshotDoesNotMixLaterChangesAndReleasesLeasesOnCorruptAudio()
    {
        using var a = new Area(); using var b = new Area(); var doc = await a.Seed(); await a.Record(doc);
        var store = new BackupExportStore(a.Db); var plan = await store.PreviewAsync();
        Assert.Equal(0L, await a.Scalar("SELECT count(*) FROM file_lease"));
        var saved = (await new SqliteContentDocumentStore(a.Db, new()).GetAsync(doc.Id))!;
        await new SqliteContentDocumentStore(a.Db, new()).SaveAsync(doc with { Title = "after preview" }, saved.RowRevision);
        var relative = (string)(await a.Scalar("SELECT relative_path FROM audio_asset"))!; await File.WriteAllBytesAsync(Path.Combine(a.Root, relative), "corrupt"u8.ToArray());
        using var bytes = new MemoryStream(); await store.ExportAsync(plan, bytes, false); bytes.Position = 0;
        var importer = new BackupImportStore(b.Db); await importer.CommitAsync(await importer.PlanAsync(bytes));
        Assert.Equal(doc.Title, (await new SqliteContentDocumentStore(b.Db, new()).GetAsync(doc.Id))!.Document.Title);
        await Assert.ThrowsAsync<PackageException>(() => store.PreviewAsync()); Assert.Equal(0L, await a.Scalar("SELECT count(*) FROM file_lease"));
    }
    [Fact]
    public async Task PendingRecordingBlocksAndDraftsTrashAreReportedButExcluded()
    {
        using var a = new Area(); var doc = await a.Seed(); var audio = new LocalAudioStore(a.Db);
        var draft = await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id), "user");
        Assert.Equal("BACKUP_RECORDING_BUSY", (await Assert.ThrowsAsync<PackageException>(() => new BackupExportStore(a.Db).PreviewAsync())).Code);
        await File.WriteAllBytesAsync(audio.CapturePath(draft.Id), Wave()); await audio.FinalizeAsync(draft.Id);
        var trashed = await a.Seed(); await new ContentTrashStore(a.Db).MoveAsync(await new ContentTrashStore(a.Db).PreviewAsync(trashed.Id));
        var plan = await new BackupExportStore(a.Db).PreviewAsync(); Assert.Equal(1, plan.Drafts); Assert.Equal(1, plan.Trash); Assert.Equal(1, plan.Contents); Assert.Equal(0, plan.Audio);
    }
    [Fact]
    public async Task CompleteTextResourceRestoresOwnershipAndUserStateAndLocalChoicesWin()
    {
        using var a = new Area(); using var b = new Area(); await a.Install();
        await a.Execute("UPDATE installed_resource SET enabled=0,priority=123; INSERT INTO resource_entry_override SELECT resource_id,entry_id,1,'2026-09-18T00:00:00Z' FROM resource_entry LIMIT 1");
        var preview = await new BackupExportStore(a.Db).PreviewAsync(); Assert.Equal(1, preview.IncludedResources); Assert.Empty(preview.MissingResources);
        using var bytes = await a.Export(); var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes); Assert.Equal(1, plan.ResourcesInstalled);
        await importer.CommitAsync(plan); Assert.Equal(4L, await b.Scalar("SELECT count(*) FROM resource_entry")); Assert.Equal(0L, await b.Scalar("SELECT enabled FROM installed_resource"));
        Assert.Equal(123L, await b.Scalar("SELECT priority FROM installed_resource")); Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM resource_entry_override"));
        await b.Execute("UPDATE installed_resource SET enabled=1,priority=4"); bytes.Position = 0; plan = await importer.PlanAsync(bytes); Assert.Equal(1, plan.ResourcesKept);
        await importer.CommitAsync(plan); Assert.Equal(4L, await b.Scalar("SELECT priority FROM installed_resource")); Assert.Equal(4L, await b.Scalar("SELECT count(*) FROM content"));
    }
    [Fact]
    public async Task DifferentResourceVersionRestoresSnapshotsWithoutDowngradingLocalResource()
    {
        using var a = new Area(); using var b = new Area(); await a.Install(); await b.Install();
        await b.Execute("UPDATE installed_resource SET version='9.0.0'"); using var bytes = await a.Export();
        var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes); Assert.Single(plan.MissingResources);
        await importer.CommitAsync(plan); Assert.Equal("9.0.0", await b.Scalar("SELECT version FROM installed_resource"));
        Assert.Equal(4L, await b.Scalar("SELECT count(*) FROM retained_content")); Assert.Equal(8L, await b.Scalar("SELECT count(*) FROM content"));
        bytes.Position = 0; plan = await importer.PlanAsync(bytes); Assert.Equal(4, plan.Reused); await importer.CommitAsync(plan);
        Assert.Equal(8L, await b.Scalar("SELECT count(*) FROM content"));
    }
    [Fact]
    public async Task ReferenceOnlyRequiresExplicitAcceptanceAndRestoresMissingDisabledRegistration()
    {
        using var a = new Area(); using var b = new Area(); await a.Install();
        // Validly represent an uninstalled source with no dependent content.
        await a.Execute("DELETE FROM search_index; DELETE FROM playback_target; DELETE FROM resource_entry; DELETE FROM content; UPDATE installed_resource SET is_present=0,enabled=0");
        var exporter = new BackupExportStore(a.Db); var preview = await exporter.PreviewAsync(); Assert.Single(preview.MissingResources);
        using var bytes = new MemoryStream(); await Assert.ThrowsAsync<PackageException>(() => exporter.ExportAsync(preview, bytes, false)); Assert.Equal(0, bytes.Length);
        await exporter.ExportAsync(preview, bytes, true); bytes.Position = 0;
        var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(bytes); Assert.Single(plan.MissingResources); await importer.CommitAsync(plan);
        Assert.Equal(0L, await b.Scalar("SELECT is_present FROM installed_resource")); Assert.Equal(0L, await b.Scalar("SELECT enabled FROM installed_resource"));
    }
    [Theory]
    [InlineData("favorite")][InlineData("folder")][InlineData("default")][InlineData("settings")][InlineData("resourceHash")]
    [InlineData("payload")][InlineData("counts")][InlineData("duplicateJson")][InlineData("traversal")][InlineData("retained")]
    public async Task BadBackupIsRejectedBeforeOpeningDestination(string fault)
    {
        using var a = new Area(); using var b = new Area(); await a.Install(); await a.Seed(); using var bytes = await a.Export();
        using var malformed = Rewrite(bytes, files =>
        {
            var collections = JsonNode.Parse(files["collections.json"])!; var state = JsonNode.Parse(files["resources.json"])!;
            if (fault == "favorite") collections["items"]!.AsArray().Add(new JsonObject { ["folderId"] = collections["folders"]![0]!["id"]!.GetValue<string>(), ["contentId"] = Guid.NewGuid().ToString(), ["sortOrder"] = 0, ["addedAtUtc"] = "2026-09-18T00:00:00Z" });
            if (fault == "folder") collections["folders"]!.AsArray().Add(collections["folders"]![0]!.DeepClone());
            if (fault == "default") collections["folders"]![0]!["systemRole"] = null;
            if (fault == "resourceHash") state["resources"]![0]!["descriptorSha256"] = new string('0', 64);
            if (fault == "payload") state["resources"]![0]!["payloadFingerprint"] = new string('0', 64);
            if (fault == "retained") state["retainedContentIds"]!.AsArray().Add(Guid.NewGuid().ToString());
            files["collections.json"] = JsonSerializer.SerializeToUtf8Bytes(collections); files["resources.json"] = JsonSerializer.SerializeToUtf8Bytes(state);
            if (fault == "settings") { var settings = JsonNode.Parse(files["settings.json"])!; settings["favorites"]!["lastFolderId"] = Guid.NewGuid().ToString(); files["settings.json"] = JsonSerializer.SerializeToUtf8Bytes(settings); }
            if (fault == "duplicateJson") files["settings.json"] = "{\"schemaVersion\":1,\"schemaVersion\":1}"u8.ToArray();
            if (fault == "traversal") files["../settings.json"] = files["settings.json"];
            if (fault == "counts") { var manifest = JsonNode.Parse(files["manifest.json"])!; manifest["counts"]!["contents"] = 999; files["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest); }
        });
        await Assert.ThrowsAsync<PackageException>(() => new BackupImportStore(b.Db).PlanAsync(malformed)); Assert.False(File.Exists(b.Db.DatabasePath));
    }
    [Fact]
    public async Task PublishedResourceWaveAndDefaultsRemainACompletePayloadAfterRestore()
    {
        using var a = new Area(); using var b = new Area(); await a.Install();
        var doc = JsonSerializer.Deserialize<ContentDocument>((string)(await a.Scalar("SELECT body_json FROM content ORDER BY id LIMIT 1"))!, ContentJson.Options)!;
        var binding = await a.Record(doc); using var bytes = await a.Export();
        // Independent publisher fixture: promote only this synthetic test track into the declared resource payload.
        using var published = Rewrite(bytes, files =>
        {
            var audio = JsonNode.Parse(files["audio/index.json"])!; audio["assets"]![0]!["origin"] = "catalog"; audio["bindings"]![0]!["sourceRole"] = "standard";
            var state = JsonNode.Parse(files["resources.json"])!; var resource = state["resources"]![0]!; var descriptor = resource["descriptor"]!;
            descriptor["audioBindingIds"] = new JsonArray(binding.ToString()); descriptor["audioDefaults"] = audio["preferences"]!.DeepClone();
            resource["descriptorSha256"] = Hash(descriptor);
            var normalized = audio.DeepClone(); foreach (var asset in normalized["assets"]!.AsArray()) { asset!.AsObject().Remove("path"); asset.AsObject().Remove("storage"); }
            var contents = JsonNode.Parse(files["contents.json"])!["contents"]!.AsArray();
            var fingerprints = new JsonArray(contents.OrderBy(n => n!["id"]!.GetValue<string>(), StringComparer.Ordinal).Select(n =>
            {
                var d = n!.DeepClone().AsObject(); foreach (var key in new[] { "createdAtUtc", "updatedAtUtc", "contentRevision", "annotationRevision", "metadataRevision" }) d.Remove(key);
                d["scenes"] = new JsonArray(d["scenes"]!.AsArray().Select(s => s!.GetValue<string>()).Distinct().Order(StringComparer.Ordinal).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
                foreach (var u in d["textUnits"]!.AsArray()) foreach (var t in u!["tokens"]!.AsArray()) t!.AsObject().Remove("modifiedAtUtc");
                return (JsonNode)new JsonObject { ["id"] = n["id"]!.GetValue<string>(), ["fingerprint"] = Hash(d) };
            }).ToArray());
            resource["payloadFingerprint"] = Hash(new JsonObject { ["descriptor"] = descriptor.DeepClone(), ["contents"] = fingerprints, ["audioIndex"] = normalized });
            files["audio/index.json"] = JsonSerializer.SerializeToUtf8Bytes(audio); files["resources.json"] = JsonSerializer.SerializeToUtf8Bytes(state);
        });
        var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(published); Assert.Equal(1, plan.ResourcesInstalled); await importer.CommitAsync(plan);
        Assert.Equal("standard", await b.Scalar("SELECT source_role FROM audio_binding")); Assert.Equal("catalog", await b.Scalar("SELECT origin FROM audio_asset"));
        await using (var audio = await new LocalAudioStore(b.Db).OpenPlaybackAsync("target", doc.TextUnits[0].Id)) Assert.Equal(1000, PcmWave.Inspect(audio.Stream).DurationMs);
        var preview = await new BackupExportStore(b.Db).PreviewAsync(); Assert.Equal(1, preview.IncludedResources); Assert.Empty(preview.MissingResources); Assert.Equal(1, preview.Audio);
    }
    [Fact]
    public async Task SingleRecordingSharesOnlyAudioBytesAndRejectsImportedTrack()
    {
        using var a = new Area(); var doc = await a.Seed(); var own = await a.Record(doc); var audio = new LocalAudioStore(a.Db); var target = await audio.GetTargetAsync(doc.TextUnits[0].Id);
        using var output = new MemoryStream(); await audio.ExportRecordingAsync(target, own, output); Assert.Equal(Wave(), output.ToArray());
        using var input = new MemoryStream(Wave()); var imported = await audio.SaveAsync(await audio.ImportAsync(target, input), "imported", false);
        using var denied = new MemoryStream(); await Assert.ThrowsAsync<InvalidOperationException>(() => audio.ExportRecordingAsync(target, imported, denied)); Assert.Equal(0, denied.Length);
    }
    [Fact]
    public async Task CancellationAndRepeatedExportIdentityAreSafe()
    {
        using var a = new Area(); using var b = new Area(); await a.Seed(); var exporter = new BackupExportStore(a.Db); var preview = await exporter.PreviewAsync();
        using var first = new MemoryStream(); using var second = new MemoryStream(); await exporter.ExportAsync(preview, first, false); await exporter.ExportAsync(preview, second, false);
        first.Position = second.Position = 0; var importer = new BackupImportStore(b.Db); var plan = await importer.PlanAsync(first);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.CommitAsync(plan, cancel.Token));
        await importer.CommitAsync(plan); await importer.CommitAsync(await importer.PlanAsync(second)); Assert.Equal(2L, await b.Scalar("SELECT count(*) FROM import_receipt")); Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM content"));
    }
    private static string Hash(JsonNode node) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(node))));
    private static string Canonical(JsonNode? n) => n switch {
        null => "null",
        JsonObject o => "{" + string.Join(',', o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Canonical(JsonValue.Create(p.Key)) + ":" + Canonical(p.Value))) + "}",
        JsonArray a => "[" + string.Join(',', a.Select(Canonical)) + "]",
        _ => n.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) };
    private static MemoryStream Rewrite(MemoryStream input, Action<Dictionary<string, byte[]>> mutate)
    {
        input.Position = 0; var files = new Dictionary<string, byte[]>(); using (var zip = new ZipArchive(input, ZipArchiveMode.Read, true))
            foreach (var e in zip.Entries) { using var s = e.Open(); using var b = new MemoryStream(); s.CopyTo(b); files[e.FullName] = b.ToArray(); }
        mutate(files); var manifest = JsonNode.Parse(files["manifest.json"])!;
        manifest["files"] = new JsonArray(files.Where(p => p.Key != "manifest.json").Select(p => (JsonNode)new JsonObject { ["path"] = p.Key, ["byteLength"] = p.Value.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(p.Value)) }).ToArray());
        files["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest); var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var (path, bytes) in files) { using var s = zip.CreateEntry(path).Open(); s.Write(bytes); }
        output.Position = 0; return output;
    }
    private static byte[] Wave() { using var stream = new MemoryStream(); PcmWave.WriteHeader(stream, 8000, 1, 16000); stream.Write(new byte[16000]); return stream.ToArray(); }
    private sealed class Area : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "HanMateBackupTests", Guid.NewGuid().ToString("N"));
        public HanMateDatabase Db { get; }
        public Area() { Directory.CreateDirectory(Root); Db = new(Path.Combine(Root, "hanmate.db")); }
        public async Task<ContentDocument> Seed(ContentKind kind = ContentKind.Text)
        {
            var body = new TextDraft("Backup-test", "你好。", kind, "textDraft.v2") { Pattern = kind == ContentKind.Grammar ? "{S}是{N}" : "", Example = kind == ContentKind.Grammar ? "你好。" : "" };
            var doc = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document;
            doc = DraftAnnotation.Correct(doc, doc.TextUnits[0].Tokens[0].Id, "ni3");
            return (await new EditorCommitStore(Db).CommitAsync(await new TextDraftStore(Db).SaveAsync(Guid.NewGuid(), body with { Annotation = doc }, 0))).Document;
        }
        public async Task<Guid> Record(ContentDocument doc)
        { var store = new LocalAudioStore(Db); var draft = await store.BeginAsync(await store.GetTargetAsync(doc.TextUnits[0].Id), "user"); await File.WriteAllBytesAsync(store.CapturePath(draft.Id), Wave()); return await store.SaveAsync(await store.FinalizeAsync(draft.Id), "engineering audio", true); }
        public async Task Install()
        { var store = new TextResourceInstaller(Db); using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures/sample-learning.hanresource")); await store.InstallAsync(await store.PlanAsync(input)); }
        public async Task<MemoryStream> Export()
        { var store = new BackupExportStore(Db); var bytes = new MemoryStream(); await store.ExportAsync(await store.PreviewAsync(), bytes, true); bytes.Position = 0; return bytes; }
        public async Task Execute(string sql) { using var c = await Db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
        public async Task<object?> Scalar(string sql) { using var c = await Db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return await cmd.ExecuteScalarAsync(); }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
