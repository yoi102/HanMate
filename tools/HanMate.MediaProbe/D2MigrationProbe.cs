using System.Security.Cryptography;
using System.Text.Json;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;

// Disposable host databases only. The returned device backup stays in private TEMP.
internal static class D2MigrationProbe
{
    internal sealed record Track(Guid Id, Guid Target, string Hash, bool Preferred);
    internal sealed record Expected(ContentDocument[] Documents, Guid Folder, Track[] Tracks);

    public static async Task RunAsync(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "HanMate-d2-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var db = new HanMateDatabase(Path.Combine(root, "hanmate.db"));
        if (args[0] == "verify-d2-content")
        {
            if (args.Length != 3) throw new ArgumentException("verify-d2-content <hanpack> <expected.json>");
            var expected = JsonSerializer.Deserialize<Expected>(await File.ReadAllTextAsync(args[2]), ContentJson.Options)!;
            var original = expected.Documents.Single(d => d.Title == "D2-Word-工程迁移");
            var importer = new ContentPackageImportStore(db);
            await using var input = File.OpenRead(args[1]);
            var plan = await importer.PlanAsync(input);
            if (plan.Added != 1 || plan.AudioAdded != 0) throw new Exception("Unselected content/audio was exported.");
            await importer.CommitAsync(plan);
            var actual = (await new SqliteContentDocumentStore(db, new()).GetAsync(original.Id))!.Document;
            if (actual.Kind != original.Kind || actual.Title != original.Title ||
                JsonSerializer.Serialize(actual.TextUnits, ContentJson.Options) != JsonSerializer.Serialize(original.TextUnits, ContentJson.Options))
                throw new Exception("Shared text/annotations/translations/IDs changed.");
            using (var zip = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read, true))
                if (zip.Entries.Any(e => e.FullName is "settings.json" or "collections.json" or "resources.json"))
                    throw new Exception("Private backup state leaked into content share.");
            input.Position = 0; var repeat = await importer.PlanAsync(input);
            if (repeat.Added != 0 || repeat.Reused != 1) throw new Exception("Repeated content import duplicated an entry.");
            await importer.CommitAsync(repeat);
            var report = new { status = "PASS", content = 1, exactTextUnits = true, unselectedAudioExcluded = true,
                noPrivateBackupFiles = true, repeat = true, privateDatabase = root };
            await File.WriteAllTextAsync(Path.Combine(root, "verification.json"), JsonSerializer.Serialize(report));
            Console.WriteLine(JsonSerializer.Serialize(report)); return;
        }
        if (args[0] == "verify-d2")
        {
            if (args.Length != 3) throw new ArgumentException("verify-d2 <backup> <expected.json>");
            var expected = JsonSerializer.Deserialize<Expected>(await File.ReadAllTextAsync(args[2]), ContentJson.Options)!;
            var importer = new BackupImportStore(db);
            await using var input = File.OpenRead(args[1]);
            var result = await importer.CommitAsync(await importer.PlanAsync(input, true));
            await VerifyAsync(db, expected);
            input.Position = 0;
            var repeat = await importer.PlanAsync(input);
            if (repeat.Added != 0 || repeat.AudioAdded != 0 || repeat.Conflicts != 0) throw new Exception("Repeat was not idempotent.");
            await importer.CommitAsync(repeat);
            await VerifyAsync(db, expected);
            var report = new { status = "PASS", verifiedContents = expected.Documents.Length, kinds = 4, tracks = expected.Tracks.Length,
                exactDocumentJson = true, trackIdsHashesAndDefaults = true, favoriteReferences = true, repeat = true,
                totalRestored = result.Added, privateDatabase = root };
            await File.WriteAllTextAsync(Path.Combine(root, "verification.json"), JsonSerializer.Serialize(report));
            Console.WriteLine(JsonSerializer.Serialize(report));
            return;
        }
        if (args.Length != 1) throw new ArgumentException("stage-d2");
        var docs = new List<ContentDocument>();
        foreach (var kind in Enum.GetValues<ContentKind>())
        {
            var draft = new TextDraft("D2-" + kind + "-工程迁移", "你好。", kind, "textDraft.v2")
                { Pattern = kind == ContentKind.Grammar ? "{S}是{N}" : "", Example = kind == ContentKind.Grammar ? "你好。" : "" };
            var annotation = DraftAnnotation.Generate(draft, BundledAnnotationLexicon.Default.Engine).Document;
            annotation = DraftAnnotation.Correct(annotation, annotation.TextUnits[0].Tokens[0].Id, "ni3");
            annotation = annotation with { TextUnits = annotation.TextUnits.Select(u => u with {
                Translations = new Dictionary<string, string> { ["en"] = "Hello. (engineering fixture)", ["ja"] = "こんにちは。（検証用）" }
            }).ToArray() };
            docs.Add((await new EditorCommitStore(db).CommitAsync(await new TextDraftStore(db).SaveAsync(
                Guid.NewGuid(), draft with { Annotation = annotation }, 0))).Document);
        }
        var course = PinyinCourse.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/pinyin-course.json")));
        var definition = EditorCommitStore.Copy(course.Data.Contents.Single(d => d.Title == "波浪")) with { Title = "D2-波浪释义-工程迁移" };
        var definitionDraft = new TextDraft(definition.Title, EditorCommitStore.PrimaryUnit(definition).Text, definition.Kind, "textDraft.v2")
            { Annotation = definition, StructuredOnly = true };
        definition = (await new EditorCommitStore(db).CommitAsync(await new TextDraftStore(db).SaveAsync(Guid.NewGuid(), definitionDraft, 0))).Document;
        docs.Add(definition);
        var favorites = new FavoriteStore(db);
        var folder = await favorites.SaveFolderAsync("D2-四类迁移", "Engineering fixture; not reviewed teaching material.");
        foreach (var doc in docs) await favorites.SetSelectionAsync(doc.Id, [folder], (await favorites.GetSelectionAsync(doc.Id)).Revision);
        var audio = new LocalAudioStore(db); var tracks = new List<Track>();
        foreach (var (doc, index) in docs.Select((d, i) => (d, i)))
        {
            var target = await audio.GetTargetAsync(doc.TextUnits[0].Id);
            using var wave = Wave(440 + index * 80);
            var id = await audio.SaveAsync(await audio.ImportAsync(target, wave), "D2 synthetic tone (not speech)", true);
            await using var lease = await audio.OpenPlaybackAsync("track", id);
            tracks.Add(new(id, target.Id, Convert.ToHexStringLower(await SHA256.HashDataAsync(lease.Stream)), true));
        }
        // Deliberately different from the current device: merging with ApplySettings off must retain device preferences.
        await new VersionedLocalStateStore(db).SaveSettingsAsync("{\"uiLanguage\":\"en\",\"reading\":{\"showPinyin\":false,\"scale\":1.75}}", 0);
        var expectation = new Expected(docs.ToArray(), folder, tracks.ToArray());
        await VerifyAsync(db, expectation);
        var exporter = new BackupExportStore(db);
        await using (var output = File.Create(Path.Combine(root, "d2-migration.hanbackup")))
            await exporter.ExportAsync(await exporter.PreviewAsync(), output, false);
        await File.WriteAllTextAsync(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(expectation, ContentJson.Options));
        await File.WriteAllBytesAsync(Path.Combine(root, "d2-invalid.hanbackup"), "Invalid engineering backup"u8.ToArray());
        Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", contents = docs.Count, tracks = tracks.Count, root }));
    }

    private static async Task VerifyAsync(HanMateDatabase db, Expected expected)
    {
        var store = new SqliteContentDocumentStore(db, new()); var audio = new LocalAudioStore(db); var favorites = new FavoriteStore(db);
        foreach (var doc in expected.Documents)
        {
            var actual = (await store.GetAsync(doc.Id))?.Document;
            if (actual is null || JsonSerializer.Serialize(actual, ContentJson.Options) != JsonSerializer.Serialize(doc, ContentJson.Options))
                throw new Exception("Document UUID/text/annotation/translation/grammar changed.");
            if (!(await favorites.GetSelectionAsync(doc.Id)).FolderIds.Contains(expected.Folder)) throw new Exception("Favorite reference changed.");
        }
        foreach (var track in expected.Tracks)
        {
            var actual = (await audio.ListTracksAsync(track.Target)).Single(t => t.Id == track.Id);
            if (actual.Preferred != track.Preferred || !actual.Eligible) throw new Exception("Audio binding/default changed.");
            await using var lease = await audio.OpenPlaybackAsync("track", track.Id);
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(lease.Stream)) != track.Hash) throw new Exception("Audio bytes changed.");
        }
    }

    private static MemoryStream Wave(int frequency)
    {
        var stream = new MemoryStream(); PcmWave.WriteHeader(stream, 8000, 1, 16000);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            for (var i = 0; i < 8000; i++) writer.Write((short)(4000 * Math.Sin(2 * Math.PI * frequency * i / 8000)));
        stream.Position = 0; return stream;
    }
}
