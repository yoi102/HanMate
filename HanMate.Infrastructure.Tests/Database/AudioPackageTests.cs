using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class AudioPackageTests
{
    [Fact]
    public async Task SelectedRecordingsRoundTripWithDeduplicatedBytesAndRepeatImportReusesMappings()
    {
        using var source = new Area(); using var target = new Area();
        var doc = await Seed(source); var first = await Record(source, doc); var second = await Record(source, doc);
        using var package = await Export(source, doc.Id, first, second);
        using (var zip = new ZipArchive(package, ZipArchiveMode.Read, true))
        {
            Assert.Equal(4, zip.Entries.Count); Assert.Single(zip.Entries, e => e.FullName.EndsWith(".wav"));
            using var s = zip.GetEntry("audio/index.json")!.Open(); var audio = JsonNode.Parse(s)!;
            Assert.Equal(2, audio["assets"]!.AsArray().Count); Assert.Equal(2, audio["bindings"]!.AsArray().Count);
        }
        package.Position = 0; var importer = new ContentPackageImportStore(target.Db); var plan = await importer.PlanAsync(package);
        Assert.Equal(2, plan.AudioAdded); Assert.Equal(16044, plan.AudioBytes);
        var result = await importer.CommitAsync(plan); Assert.Equal(doc.Id, Assert.Single(result.ContentIds));
        var store = new LocalAudioStore(target.Db); var tracks = await store.ListTracksAsync(doc.TextUnits[0].Id);
        Assert.Equal(2, tracks.Count); Assert.All(tracks, t => { Assert.Equal("imported", t.SourceRole); Assert.True(t.Eligible); });
        Assert.Single(tracks, t => t.Preferred);
        await using (var lease = await store.OpenPlaybackAsync("target", doc.TextUnits[0].Id))
        { using var data = new MemoryStream(); await lease.Stream.CopyToAsync(data); Assert.Equal(Wave(), data.ToArray()); }
        Assert.Equal(0, await Scalar(target, "SELECT count(*) FROM file_lease"));
        // A repeated package must retain a local preference even when the package prefers the other track.
        var chosen = tracks.Single(t => !t.Preferred); await store.UpdateTrackAsync(await store.GetTargetAsync(doc.TextUnits[0].Id), chosen.Id, "default");
        package.Position = 0; plan = await importer.PlanAsync(package); Assert.Equal(0, plan.AudioAdded); Assert.Equal(2, plan.AudioReused);
        await importer.CommitAsync(plan); Assert.Equal(chosen.Id, (await store.ListTracksAsync(doc.TextUnits[0].Id)).Single(t => t.Preferred).Id);
        Assert.Single(Directory.GetFiles(Path.Combine(target.Root, "audio/packages")));
        // Receiving a voice never turns it into this device owner's recording.
        Assert.All((await new ContentShareStore(target.Db).PreviewAsync([doc.Id])).AudioTracks, t => Assert.False(t.CanShare));
    }

    [Fact]
    public async Task ContentCollisionRemapsAudioTargetsAndDeletedBindingsCanBeImportedAgain()
    {
        using var source = new Area(); using var target = new Area(); var doc = await Seed(source, ContentKind.Grammar); var binding = await Record(source, doc);
        await new SqliteContentDocumentStore(target.Db, new()).SaveAsync(doc with { Title = "keep local" }, 0);
        using var package = await Export(source, doc.Id, binding); var importer = new ContentPackageImportStore(target.Db);
        var plan = await importer.PlanAsync(package); Assert.Equal(1, plan.Conflicts); var result = await importer.CommitAsync(plan);
        var copy = (await new SqliteContentDocumentStore(target.Db, new()).GetAsync(result.ContentIds[0]))!.Document;
        Assert.NotEqual(doc.Id, copy.Id); Assert.NotEqual(doc.TextUnits[0].Id, copy.TextUnits[0].Id);
        var store = new LocalAudioStore(target.Db); var track = Assert.Single(await store.ListTracksAsync(copy.TextUnits[0].Id));
        await store.UpdateTrackAsync(await store.GetTargetAsync(copy.TextUnits[0].Id), track.Id, "remove");
        package.Position = 0; plan = await importer.PlanAsync(package); Assert.Equal(0, plan.Added); Assert.Equal(1, plan.AudioAdded);
        await importer.CommitAsync(plan); Assert.Single(await store.ListTracksAsync(copy.TextUnits[0].Id));
        Assert.Equal("keep local", (await new SqliteContentDocumentStore(target.Db, new()).GetAsync(doc.Id))!.Document.Title);
    }

    [Fact]
    public async Task StalePronunciationRemainsNeedsReviewAndCannotBecomeAutomaticDefault()
    {
        using var source = new Area(); using var target = new Area(); var doc = await Seed(source); var binding = await Record(source, doc);
        var editor = new EditorCommitStore(source.Db); var editing = await editor.StartAsync((await new SqliteContentDocumentStore(source.Db, new()).GetAsync(doc.Id))!);
        var corrected = DraftAnnotation.Correct(editing.Body.Annotation!, doc.TextUnits[0].Tokens[0].Id, "ni4");
        editing = await new TextDraftStore(source.Db).SaveAsync(editing.Id, editing.Body with { Annotation = corrected }, editing.Revision);
        await editor.CommitAsync(editing);
        using var package = await Export(source, doc.Id, binding); var importer = new ContentPackageImportStore(target.Db);
        var plan = await importer.PlanAsync(package); Assert.Equal(1, plan.AudioNeedsReview); await importer.CommitAsync(plan);
        var store = new LocalAudioStore(target.Db); var track = Assert.Single(await store.ListTracksAsync(doc.TextUnits[0].Id));
        Assert.False(track.Eligible); Assert.False(track.Preferred);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenPlaybackAsync("target", doc.TextUnits[0].Id));
        await using var audition = await store.OpenPlaybackAsync("track", track.Id); Assert.Equal(1000, audition.DurationMs);
    }

    [Fact]
    public async Task OnlyExplicitlySelectedOwnedRecordingsAreExportedWithSeparateRightsConfirmation()
    {
        using var area = new Area(); var doc = await Seed(area); var recording = await Record(area, doc);
        var store = new LocalAudioStore(area.Db); var target = await store.GetTargetAsync(doc.TextUnits[0].Id);
        using var input = new MemoryStream(Wave()); var imported = await store.SaveAsync(await store.ImportAsync(target, input), "foreign", false);
        var share = new ContentShareStore(area.Db); var plan = await share.PreviewAsync([doc.Id]);
        Assert.Equal(2, plan.Audio); using var output = new MemoryStream();
        await Assert.ThrowsAsync<PackageException>(() => share.ExportAsync(plan, output, false, [recording], false));
        await Assert.ThrowsAsync<PackageException>(() => share.ExportAsync(plan, output, false, [imported], true));
        await Assert.ThrowsAsync<PackageException>(() => share.ExportAsync(plan, output, false, [Guid.NewGuid()], true)); Assert.Equal(0, output.Length);
        using var text = await Export(area, doc.Id); using var zip = new ZipArchive(text); Assert.Equal(3, zip.Entries.Count);
        using var selected = await Export(area, doc.Id, recording); using var selectedZip = new ZipArchive(selected);
        using var index = selectedZip.GetEntry("audio/index.json")!.Open(); Assert.Single(JsonNode.Parse(index)!["bindings"]!.AsArray());
    }

    [Fact]
    public async Task FailureAfterFileInstallationRollsBackDatabaseAndRetryReusesVerifiedFile()
    {
        using var source = new Area(); using var target = new Area(); var doc = await Seed(source); var binding = await Record(source, doc);
        using var package = await Export(source, doc.Id, binding); var importer = new ContentPackageImportStore(target.Db); var plan = await importer.PlanAsync(package);
        await Execute(target, "CREATE TRIGGER fail_audio_package BEFORE INSERT ON import_operation BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => importer.CommitAsync(plan));
        foreach (var table in new[] { "content", "audio_asset", "audio_binding", "audio_preference", "import_mapping", "import_receipt", "import_operation" }) Assert.Equal(0, await Scalar(target, "SELECT count(*) FROM " + table));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(target.Root, "audio/packages"))); Assert.Equal(Wave(), await File.ReadAllBytesAsync(path));
        await Execute(target, "DROP TRIGGER fail_audio_package"); await importer.CommitAsync(plan); await importer.CommitAsync(plan);
        Assert.Equal(1, await Scalar(target, "SELECT count(*) FROM audio_binding")); Assert.Single(Directory.GetFiles(Path.Combine(target.Root, "audio/packages")));
    }

    [Fact]
    public async Task CorruptExistingFileIsNeverOverwrittenAndStaleOrCancelledPlanDoesNotPublishAudio()
    {
        using var source = new Area(); using var target = new Area(); var doc = await Seed(source); var binding = await Record(source, doc);
        using var package = await Export(source, doc.Id, binding); var importer = new ContentPackageImportStore(target.Db); var plan = await importer.PlanAsync(package);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.CommitAsync(plan, cancel.Token));
        Assert.False(Directory.Exists(Path.Combine(target.Root, "audio/packages")));
        await Execute(target, "UPDATE app_state SET data_epoch=data_epoch+1"); await Assert.ThrowsAsync<PackageException>(() => importer.CommitAsync(plan));
        package.Position = 0; plan = await importer.PlanAsync(package);
        var path = Path.Combine(target.Root, "audio/packages", Convert.ToHexStringLower(SHA256.HashData(Wave())) + ".wav"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, "preserve corrupt evidence"u8.ToArray());
        await Assert.ThrowsAnyAsync<Exception>(() => importer.CommitAsync(plan)); Assert.Equal("preserve corrupt evidence", await File.ReadAllTextAsync(path));
        Assert.Equal(0, await Scalar(target, "SELECT count(*) FROM content"));
    }

    [Theory]
    [InlineData("wave")][InlineData("metadata")][InlineData("target")][InlineData("hash")][InlineData("orphan")]
    [InlineData("default")][InlineData("duplicate")][InlineData("codec")][InlineData("stale")][InlineData("extra")]
    public async Task MalformedAudioPackagesAreRejectedBeforeCreatingDestinationDatabase(string fault)
    {
        using var source = new Area(); using var target = new Area(); var doc = await Seed(source); var binding = await Record(source, doc);
        using var package = await Export(source, doc.Id, binding);
        using var bad = Rewrite(package, files =>
        {
            var audio = JsonNode.Parse(files["audio/index.json"])!; var asset = audio["assets"]![0]!; var track = audio["bindings"]![0]!;
            if (fault == "wave")
            {
                var old = asset["path"]!.GetValue<string>(); var bytes = files[old]; bytes[0] = 0; var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                files.Remove(old); asset["path"] = "audio/" + hash + ".wav"; asset["sha256"] = hash; files[asset["path"]!.GetValue<string>()] = bytes;
            }
            if (fault == "metadata") asset["sampleRate"] = 44100;
            if (fault == "target") track["targetId"] = Guid.NewGuid().ToString();
            if (fault == "hash") asset["sha256"] = new string('0',64);
            if (fault == "orphan") { audio["bindings"] = new JsonArray(); audio["preferences"] = new JsonArray(); }
            if (fault == "default") audio["preferences"]![0]!["targetId"] = Guid.NewGuid().ToString();
            if (fault == "duplicate") asset["id"] = track["id"]!.GetValue<string>();
            if (fault == "codec") asset["codec"] = "mp3";
            if (fault == "stale") track["boundTextHash"] = new string('0',64);
            if (fault == "extra") files["audio/" + new string('f',64) + ".wav"] = Wave();
            files["audio/index.json"] = Encoding.UTF8.GetBytes(audio.ToJsonString());
        });
        await Assert.ThrowsAsync<PackageException>(() => new ContentPackageImportStore(target.Db).PlanAsync(bad)); Assert.False(File.Exists(target.Db.DatabasePath));
    }
    private static async Task<ContentDocument> Seed(Area area, ContentKind kind = ContentKind.Text)
    {
        var body = new TextDraft("audio package", "你好。", kind, "textDraft.v2") { Pattern = kind == ContentKind.Grammar ? "{S}是{N}" : "", Example = kind == ContentKind.Grammar ? "你好。" : "" };
        var doc = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document;
        doc = doc with { Source = doc.Source with { CanShare = true } };
        return (await new EditorCommitStore(area.Db).CommitAsync(await new TextDraftStore(area.Db).SaveAsync(Guid.NewGuid(), body with { Annotation = doc }, 0))).Document;
    }
    private static async Task<Guid> Record(Area area, ContentDocument doc)
    {
        var store = new LocalAudioStore(area.Db); var draft = await store.BeginAsync(await store.GetTargetAsync(doc.TextUnits[0].Id), "user");
        await File.WriteAllBytesAsync(store.CapturePath(draft.Id), Wave()); return await store.SaveAsync(await store.FinalizeAsync(draft.Id), "test voice", true);
    }
    private static byte[] Wave()
    { using var stream = new MemoryStream(); stream.SetLength(16044); PcmWave.WriteHeader(stream,8000,1,16000); return stream.ToArray(); }
    private static async Task<MemoryStream> Export(Area area, Guid content, params Guid[] audio)
    { var share = new ContentShareStore(area.Db); var stream = new MemoryStream(); await share.ExportAsync(await share.PreviewAsync([content]), stream, false, audio, true); stream.Position = 0; return stream; }
    private static MemoryStream Rewrite(MemoryStream input, Action<Dictionary<string,byte[]>> action)
    {
        input.Position = 0; var files = new Dictionary<string,byte[]>(); using (var zip = new ZipArchive(input,ZipArchiveMode.Read,true)) foreach(var e in zip.Entries) { using var stream=e.Open(); using var bytes=new MemoryStream(); stream.CopyTo(bytes); files[e.FullName]=bytes.ToArray(); }
        action(files); var manifest=JsonNode.Parse(files["manifest.json"])!;
        manifest["files"]=new JsonArray(files.Where(p=>p.Key!="manifest.json").Select(p=>(JsonNode)new JsonObject{["path"]=p.Key,["byteLength"]=p.Value.Length,["sha256"]=Convert.ToHexStringLower(SHA256.HashData(p.Value))}).ToArray());
        files["manifest.json"]=Encoding.UTF8.GetBytes(manifest.ToJsonString()); var result=new MemoryStream();
        using(var zip=new ZipArchive(result,ZipArchiveMode.Create,true))foreach(var (path,bytes) in files){using var s=zip.CreateEntry(path).Open();s.Write(bytes);} result.Position=0;return result;
    }
    private static async Task<long> Scalar(Area area, string sql) { using var c=await area.Db.OpenConnectionAsync();using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    private static async Task Execute(Area area,string sql) { using var c=await area.Db.OpenConnectionAsync();using var cmd=c.CreateCommand();cmd.CommandText=sql;await cmd.ExecuteNonQueryAsync(); }
    private sealed class Area : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"HanMate-audio-package-tests",Guid.NewGuid().ToString("N"));
        public HanMateDatabase Db { get; }
        public Area() { Directory.CreateDirectory(Root); Db=new(Path.Combine(Root,"hanmate.db")); }
        public void Dispose() { if(Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
}
