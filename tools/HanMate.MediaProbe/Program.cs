using System.Text.Json;
using HanMate.App.Audio;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;

if (args.FirstOrDefault() == "stage-d3") { await D3ReadingProbe.RunAsync(); return; }
if (args.FirstOrDefault() == "record-probe") { await RecordingProbe.RunAsync(); return; }
if (args.FirstOrDefault() is "stage-d2" or "verify-d2" or "verify-d2-content" or "verify-w5-android-content") { await D2MigrationProbe.RunAsync(args); return; }
if (args.FirstOrDefault() == "verify-w5-resource-upgrade") { await W5ResourceUpgradeProbe.RunAsync(args); return; }

var root = Path.Combine(Path.GetTempPath(), "HanMate-media-probe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
if (args.Length > 0)
{
    if (args.Length != 3 || args[0] != "verify-return") throw new ArgumentException("Usage: verify-return <android-backup> <expected.json>");
    var expected = JsonSerializer.Deserialize<MigrationExpected>(await File.ReadAllTextAsync(args[2]))!;
    var destination = new HanMateDatabase(Path.Combine(root, "return", "hanmate.db")); var receiver = new BackupImportStore(destination);
    using var incoming = File.OpenRead(args[1]); await receiver.CommitAsync(await receiver.PlanAsync(incoming));
    var actual = (await new SqliteContentDocumentStore(destination, new ContentDocumentValidator()).GetAsync(expected.ContentId))?.Document ?? throw new Exception("Migration content missing.");
    if (JsonSerializer.Serialize(actual, ContentJson.Options) != expected.ContentJson) throw new Exception("Content/correction/translation changed across platforms.");
    var folders = await new FavoriteStore(destination).GetFoldersAsync();
    var selection = await new FavoriteStore(destination).GetSelectionAsync(expected.ContentId);
    if (!folders.Any(f => f.Name == "Media-migration" && selection.FolderIds.Contains(f.Id))) throw new Exception("Favorite relationship missing.");
    var store = new LocalAudioStore(destination); var tracks = await store.ListTracksAsync(expected.TargetId); var hashes = new List<string>();
    foreach (var track in tracks)
    { await using var lease = await store.OpenPlaybackAsync("track", track.Id); hashes.Add(Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(lease.Stream))); }
    if (!hashes.Order().SequenceEqual(expected.AudioHashes.Order())) throw new Exception("Audio bytes changed across platforms.");
    if (tracks.Count(t => t.Preferred) != 1) throw new Exception("Preferred track missing.");
    Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", direction = "Windows-Android-Windows", content = 1, manualCorrection = true, favorites = 1, audio = hashes.Count, privateDatabase = root }));
    return;
}
var db = new HanMateDatabase(Path.Combine(root, "hanmate.db")); var drafts = new TextDraftStore(db);
var text = new TextDraft("Compressed audio engineering probe", "你好。", ContentKind.Word, "textDraft.v2");
var doc = DraftAnnotation.Generate(text, BundledAnnotationLexicon.Default.Engine).Document;
doc = DraftAnnotation.Correct(doc, doc.TextUnits[0].Tokens[0].Id, "ni3");
doc = (await new EditorCommitStore(db).CommitAsync(await drafts.SaveAsync(Guid.NewGuid(), text with { Annotation = doc }, 0))).Document;
var audio = new LocalAudioStore(db); var target = await audio.GetTargetAsync(doc.TextUnits[0].Id); var results = new List<object>();
var audioHashes = new List<string>();
foreach (var extension in new[] { "mp3", "m4a" })
{
    await using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone." + extension));
    var draft = await AudioFileImport.ImportAsync(audio, target, input, root, CancellationToken.None);
    if (draft.Wave is not { SampleRate: 44100, Channels: 1 } wave || wave.DurationMs is < 950 or > 1200) throw new Exception("Unexpected native decode format/duration.");
    var id = await audio.SaveAsync(draft, extension + " engineering tone", true);
    await using var lease = await audio.OpenPlaybackAsync("track", id); var info = PcmWave.Inspect(lease.Stream);
    lease.Stream.Position = info.DataOffset; var samples = new byte[4096]; await lease.Stream.ReadExactlyAsync(samples);
    if (samples.All(b => b == 0)) throw new Exception("The native decoder produced silence.");
    lease.Stream.Position = 0; audioHashes.Add(Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(lease.Stream)));
    results.Add(new { format = extension, status = "PASS", sampleRate = info.SampleRate, channels = info.Channels, durationMs = info.DurationMs });
}
using (var input = new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.mp3"))[..^9]))
{
    try { await AudioFileImport.ImportAsync(audio, target, input, root, CancellationToken.None); throw new Exception("Truncation was accepted."); }
    catch (InvalidDataException) { results.Add(new { format = "truncated-mp3", status = "PASS" }); }
}
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.m4a"));
    try { await AudioFileImport.ImportAsync(audio, target, input, root, cancelled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { results.Add(new { format = "cancelled-import", status = "PASS" }); }
}
if ((await audio.ListDraftsAsync(target.Id)).Count != 0 || Directory.EnumerateDirectories(root, "decode-*").Any()) throw new Exception("Failed imports left drafts/staging files.");
var favorites = new FavoriteStore(db); var folder = await favorites.SaveFolderAsync("Media-migration", "Engineering migration fixture; not teaching material.");
await favorites.SetSelectionAsync(doc.Id, [folder], (await favorites.GetSelectionAsync(doc.Id)).Revision);
var exporter = new BackupExportStore(db); using var backup = new MemoryStream(); await exporter.ExportAsync(await exporter.PreviewAsync(), backup, true);
await File.WriteAllBytesAsync(Path.Combine(root, "migration.hanbackup"), backup.ToArray());
await File.WriteAllTextAsync(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(new MigrationExpected(doc.Id, target.Id, JsonSerializer.Serialize(doc, ContentJson.Options), audioHashes)));
backup.Position = 0; var restored = new HanMateDatabase(Path.Combine(root, "restored", "hanmate.db")); var importer = new BackupImportStore(restored);
await importer.CommitAsync(await importer.PlanAsync(backup));
if ((await new BackupExportStore(restored).PreviewAsync()).Audio != 2) throw new Exception("Normalized audio backup did not restore.");
results.Add(new { format = "backup-roundtrip", status = "PASS" });
var report = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(root, "report.json"), report); Console.WriteLine(report); Console.WriteLine(root);

internal sealed record MigrationExpected(Guid ContentId, Guid TargetId, string ContentJson, IReadOnlyList<string> AudioHashes);
