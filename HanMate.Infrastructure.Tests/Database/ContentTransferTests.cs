using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class ContentTransferTests
{
    [Fact]
    public async Task FourKindsRoundTripOnlySelectedSavedContentAndNoPrivateTables()
    {
        using var a = new TestDatabaseDirectory(); using var b = new TestDatabaseDirectory(); var source = new HanMateDatabase(a.DatabasePath); var target = new HanMateDatabase(b.DatabasePath);
        var docs = new List<ContentDocumentSnapshot>(); foreach (var kind in Enum.GetValues<ContentKind>()) docs.Add(await Seed(source, kind));
        await Seed(source, ContentKind.Text, "private unselected");
        await new EditorCommitStore(source).StartAsync(docs[0]);
        var share = new ContentShareStore(source); var preview = await share.PreviewAsync(docs.Select(d => d.Document.Id).ToArray());
        Assert.Equal(1, preview.Drafts); Assert.Equal(0, preview.Blocked);
        using var bytes = new MemoryStream(); await share.ExportAsync(preview, bytes, false); bytes.Position = 0;
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Read, true)) Assert.Equal(new[] { "audio/index.json", "contents.json", "manifest.json" }, zip.Entries.Select(e => e.FullName).Order());
        bytes.Position = 0; var importer = new ContentPackageImportStore(target); var plan = await importer.PlanAsync(bytes); Assert.Equal(4, plan.Added);
        var result = await importer.CommitAsync(plan); Assert.Equal(4, result.Added);
        foreach (var d in docs)
        {
            var loaded = (await new SqliteContentDocumentStore(target, new()).GetAsync(d.Document.Id))!.Document;
            Assert.Equal(JsonSerializer.Serialize(d.Document, ContentJson.Options), JsonSerializer.Serialize(loaded, ContentJson.Options));
        }
        Assert.Equal(0, await Count(target, "draft")); Assert.Equal(0, await Count(target, "installed_resource")); Assert.Equal(0, await Count(target, "favorite_item"));
        Assert.Equal(4, await Count(target, "content")); Assert.True(await Count(target, "playback_target") > 4);
        Assert.Equal(result.ContentIds, (await importer.CommitAsync(plan)).ContentIds);
        bytes.Position = 0; var repeat = await importer.PlanAsync(bytes); Assert.Equal(0, repeat.Added); Assert.Equal(4, repeat.Reused); await importer.CommitAsync(repeat);
        Assert.Equal(4, await Count(target, "content")); Assert.Equal(1, await Count(target, "import_receipt")); Assert.Equal(2, await Count(target, "import_operation"));
    }
    [Fact]
    public async Task ConflictingGrammarRemapsAllIdsAndRepeatReusesMappedCopy()
    {
        using var a = new TestDatabaseDirectory(); using var b = new TestDatabaseDirectory(); var source = new HanMateDatabase(a.DatabasePath); var target = new HanMateDatabase(b.DatabasePath);
        var doc = (await Seed(source, ContentKind.Grammar)).Document; var local = doc with { Title = "Local edit" };
        await new SqliteContentDocumentStore(target, new()).SaveAsync(local, 0);
        using var bytes = await Export(source, doc.Id); var store = new ContentPackageImportStore(target); var plan = await store.PlanAsync(bytes);
        Assert.Equal(1, plan.Conflicts); var result = await store.CommitAsync(plan); var id = Assert.Single(result.ContentIds); Assert.NotEqual(doc.Id, id);
        var imported = (await new SqliteContentDocumentStore(target, new()).GetAsync(id))!.Document;
        Assert.Empty(Ids(doc).Intersect(Ids(imported))); Assert.Equal(doc.Source, imported.Source);
        Assert.Equal(imported.TextUnits.Where(u => u.Role == TextUnitRole.GrammarExplanation).Select(u => u.Id), imported.Grammar!.ExplanationUnitIds);
        Assert.Contains(imported.TextUnits.SelectMany(u => u.Tokens), t => t.Locked);
        Assert.Equal("Local edit", (await new SqliteContentDocumentStore(target, new()).GetAsync(doc.Id))!.Document.Title);
        bytes.Position = 0; var repeat = await store.PlanAsync(bytes); Assert.Equal(1, repeat.Reused); await store.CommitAsync(repeat); Assert.Equal(2, await Count(target, "content"));
        var saved = (await new SqliteContentDocumentStore(target, new()).GetAsync(id))!;
        await new SqliteContentDocumentStore(target, new()).SaveAsync(imported with { Title = "edited copy" }, saved.RowRevision);
        bytes.Position = 0; var changed = await store.PlanAsync(bytes); Assert.Equal(1, changed.Added); await store.CommitAsync(changed);
        Assert.Equal(3, await Count(target, "content")); Assert.Equal("edited copy", (await new SqliteContentDocumentStore(target, new()).GetAsync(id))!.Document.Title);
    }
    [Fact]
    public async Task NestedCollisionAloneClonesTheWholeGraph()
    {
        using var a = new TestDatabaseDirectory(); using var b = new TestDatabaseDirectory(); var source = new HanMateDatabase(a.DatabasePath); var target = new HanMateDatabase(b.DatabasePath);
        var doc = (await Seed(source)).Document;
        await new SqliteContentDocumentStore(target, new()).SaveAsync(doc with { Id = Guid.NewGuid(), Title = "different root" }, 0);
        using var bytes = await Export(source, doc.Id); var importer = new ContentPackageImportStore(target); var plan = await importer.PlanAsync(bytes); Assert.Equal(1, plan.Conflicts);
        var result = await importer.CommitAsync(plan); Assert.NotEqual(doc.Id, result.ContentIds[0]);
    }
    [Fact]
    public async Task ArchiveRecompressionIsAllowedButChangedManifestIdentityIsRejected()
    {
        using var a = new TestDatabaseDirectory(); using var b = new TestDatabaseDirectory(); var source = new HanMateDatabase(a.DatabasePath); var target = new HanMateDatabase(b.DatabasePath);
        var doc = await Seed(source); using var bytes = await Export(source, doc.Document.Id); var importer = new ContentPackageImportStore(target);
        await importer.CommitAsync(await importer.PlanAsync(bytes));
        using var repacked = Rewrite(bytes, _ => { }, false); var plan = await importer.PlanAsync(repacked); Assert.Equal(1, plan.Reused); await importer.CommitAsync(plan);
        using var changed = Rewrite(bytes, files => { var content = JsonNode.Parse(files["contents.json"])!; content["contents"]![0]!["title"] = "different"; files["contents.json"] = Encoding.UTF8.GetBytes(content.ToJsonString()); }, true);
        Assert.Equal("PACKAGE_ID_COLLISION", (await Assert.ThrowsAsync<PackageException>(() => importer.PlanAsync(changed))).Code);
    }
    [Fact]
    public async Task TrashNeverGetsSilentlyRestoredByImportAndStalePlanCannotCommit()
    {
        using var a = new TestDatabaseDirectory(); var db = new HanMateDatabase(a.DatabasePath); var doc = await Seed(db); using var bytes = await Export(db, doc.Document.Id);
        var importer = new ContentPackageImportStore(db); var plan = await importer.PlanAsync(bytes); var trash = new ContentTrashStore(db); await trash.MoveAsync(await trash.PreviewAsync(doc.Document.Id));
        await Assert.ThrowsAsync<PackageException>(() => importer.CommitAsync(plan));
        bytes.Position = 0; plan = await importer.PlanAsync(bytes); Assert.Equal(1, plan.Conflicts); await importer.CommitAsync(plan); Assert.Single(await trash.ListAsync()); Assert.Equal(2, await Count(db, "content"));
    }
    [Fact]
    public async Task FailureAtReceiptCommitAndCancellationLeaveNoPartialGraphs()
    {
        using var a = new TestDatabaseDirectory(); using var b = new TestDatabaseDirectory(); var source = new HanMateDatabase(a.DatabasePath); var target = new HanMateDatabase(b.DatabasePath);
        var doc = await Seed(source); using var bytes = await Export(source, doc.Document.Id); var importer = new ContentPackageImportStore(target); var plan = await importer.PlanAsync(bytes);
        using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.CommitAsync(plan, cts.Token));
        using (var c = await target.OpenConnectionAsync()) { using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TRIGGER fail_import BEFORE INSERT ON import_operation BEGIN SELECT RAISE(ABORT,'test'); END;"; await cmd.ExecuteNonQueryAsync(); }
        await Assert.ThrowsAnyAsync<Exception>(() => importer.CommitAsync(plan));
        foreach (var table in new[] { "content", "playback_target", "search_index", "import_receipt", "import_mapping", "import_operation" }) Assert.Equal(0, await Count(target, table));
    }
    [Fact]
    public async Task RightsAttestationOnlyAppliesToPersonalInputAndDoesNotMutateSource()
    {
        using var a = new TestDatabaseDirectory(); var db = new HanMateDatabase(a.DatabasePath); var saved = await Seed(db, allowed: false); var share = new ContentShareStore(db);
        var plan = await share.PreviewAsync([saved.Document.Id]); Assert.Equal(1, plan.NeedAttestation);
        using var output = new MemoryStream(); await Assert.ThrowsAsync<PackageException>(() => share.ExportAsync(plan, output, false)); Assert.Equal(0, output.Length);
        await share.ExportAsync(plan, output, true); Assert.False((await new SqliteContentDocumentStore(db, new()).GetAsync(saved.Document.Id))!.Document.Source.CanShare);
        await new SqliteContentDocumentStore(db, new()).SaveAsync(saved.Document with { Source = saved.Document.Source with { ResourceId = Guid.NewGuid(), ResourceVersion = "1.0.0", EntryId = "test" } }, saved.RowRevision);
        plan = await share.PreviewAsync([saved.Document.Id]); Assert.Equal(1, plan.Blocked);
        await Assert.ThrowsAsync<PackageException>(() => share.ExportAsync(plan, output, true));
        var trash = new ContentTrashStore(db); await trash.MoveAsync(await trash.PreviewAsync(saved.Document.Id)); Assert.Empty(await share.ListAsync());
        await Assert.ThrowsAsync<PackageException>(() => share.PreviewAsync([saved.Document.Id]));
    }
    [Theory]
    [InlineData("extra")][InlineData("hash")][InlineData("counts")][InlineData("duplicate-key")][InlineData("duplicate-id")][InlineData("origin")][InlineData("path")][InlineData("audio")]
    public async Task InvalidOrPrivacyLeakingPackagesAreRejectedBeforeWriting(string fault)
    {
        using var a = new TestDatabaseDirectory(); using var b = new TestDatabaseDirectory(); var db = new HanMateDatabase(a.DatabasePath); var saved = await Seed(db); using var bytes = await Export(db, saved.Document.Id);
        using var bad = Rewrite(bytes, files =>
        {
            if (fault == "extra") files["settings.json"] = "{}"u8.ToArray();
            if (fault == "path") { files["../contents.json"] = files["contents.json"]; files.Remove("contents.json"); }
            if (fault == "hash") files["contents.json"][10] ^= 1;
            if (fault == "duplicate-key") files["contents.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["contents.json"]).Replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"schemaVersion\":2"));
            if (fault is "duplicate-id" or "origin") { var json = JsonNode.Parse(files["contents.json"])!; if (fault == "origin") json["contents"]![0]!["origin"] = "resource"; else json["contents"]!.AsArray().Add(json["contents"]![0]!.DeepClone()); files["contents.json"] = Encoding.UTF8.GetBytes(json.ToJsonString()); }
            if (fault == "counts") { var m = JsonNode.Parse(files["manifest.json"])!; m["counts"]!["contents"] = 9; files["manifest.json"] = Encoding.UTF8.GetBytes(m.ToJsonString()); }
            if (fault == "audio") { var j = JsonNode.Parse(files["audio/index.json"])!; j["preferences"]!.AsArray().Add(new JsonObject { ["targetId"] = Guid.NewGuid().ToString(), ["bindingId"] = Guid.NewGuid().ToString() }); files["audio/index.json"] = Encoding.UTF8.GetBytes(j.ToJsonString()); }
        }, fault != "hash");
        await Assert.ThrowsAsync<PackageException>(() => new ContentPackageImportStore(new(b.DatabasePath)).PlanAsync(bad));
        Assert.False(File.Exists(b.DatabasePath));
    }
    private static IEnumerable<Guid> Ids(ContentDocument d) => new[] { d.Id }.Concat(d.TextUnits.SelectMany(u => new[] { u.Id }.Concat(u.Tokens.Select(t => t.Id)).Concat(u.Segments.Select(s => s.Id))));
    private static async Task<ContentDocumentSnapshot> Seed(HanMateDatabase db, ContentKind kind = ContentKind.Text, string title = "test", bool allowed = true)
    {
        var body = new TextDraft(title, "你好。", kind, "textDraft.v2") { Pattern = "{S}是{N}", Example = "你好。" };
        if (kind != ContentKind.Grammar) body = body with { Pattern = "", Example = "" };
        var doc = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document;
        doc = DraftAnnotation.Correct(doc, doc.TextUnits[0].Tokens[0].Id, "ni3");
        doc = doc with { Source = doc.Source with { CanShare = allowed }, TextUnits = doc.TextUnits.Select(u => u with { Translations = new Dictionary<string,string> { ["ja"] = "こんにちは", ["en"] = "Hello" } }).ToArray() };
        return await new EditorCommitStore(db).CommitAsync(await new TextDraftStore(db).SaveAsync(Guid.NewGuid(), body with { Annotation = doc }, 0));
    }
    private static async Task<MemoryStream> Export(HanMateDatabase db, params Guid[] ids)
    { var store = new ContentShareStore(db); var output = new MemoryStream(); await store.ExportAsync(await store.PreviewAsync(ids), output, false); output.Position = 0; return output; }
    private static MemoryStream Rewrite(MemoryStream input, Action<Dictionary<string,byte[]>> change, bool hashes)
    {
        input.Position = 0; var files = new Dictionary<string,byte[]>(); using (var zip = new ZipArchive(input, ZipArchiveMode.Read, true)) foreach(var entry in zip.Entries) { using var stream = entry.Open(); using var data = new MemoryStream(); stream.CopyTo(data); files[entry.FullName] = data.ToArray(); }
        change(files);
        if (hashes) { var manifest = JsonNode.Parse(files["manifest.json"])!; foreach(var node in manifest["files"]!.AsArray()) { var path = node!["path"]!.GetValue<string>(); if (!files.TryGetValue(path, out var data)) continue; node["byteLength"] = data.Length; node["sha256"] = Convert.ToHexStringLower(SHA256.HashData(data)); } files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()); }
        var result = new MemoryStream(); using (var zip = new ZipArchive(result, ZipArchiveMode.Create, true)) foreach(var (path,data) in files.Reverse()) { using var stream = zip.CreateEntry(path, CompressionLevel.NoCompression).Open(); stream.Write(data); } result.Position = 0; return result;
    }
    private static async Task<long> Count(HanMateDatabase db, string table)
    { using var c = await db.OpenConnectionAsync(); using var command = c.CreateCommand(); command.CommandText = "SELECT count(*) FROM " + table; return Convert.ToInt64(await command.ExecuteScalarAsync()); }
}
