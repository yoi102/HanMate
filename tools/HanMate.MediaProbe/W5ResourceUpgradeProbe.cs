using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

// Checks the exact generated files against the production installer in a fresh, disposable database.
internal static class W5ResourceUpgradeProbe
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length is not (3 or 4 or 5)) throw new ArgumentException("verify-w5-resource-upgrade <v1.hanresource> <v1.0.1.hanresource> [v1.0.2-audio.hanresource [v1.0.3-title.hanresource]]");
        var root = Path.Combine(Path.GetTempPath(), "HanMate-w5-resource-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var db = new HanMateDatabase(Path.Combine(root, "hanmate.db"));
        var installer = new TextResourceInstaller(db);
        await using (var first = File.OpenRead(args[1]))
        {
            var plan = await installer.PlanAsync(first);
            if (plan.Version != "1.0.0" || plan.ContentCount != 4) throw new Exception("Unexpected first resource package.");
            await installer.InstallAsync(plan);
        }
        var management = new ResourceManagementStore(db);
        var installed = (await management.GetAsync()).Single();
        var id = installed.State.ResourceId;
        var entries = await management.GetEntriesAsync(id);
        if (entries.Count != 4) throw new Exception("First resource did not install four entries.");
        var withdrawn = entries[0];
        await new ResourceStateStore(db).SetEntryRemovedAsync(id, withdrawn.EntryId, true, installed.State.RowRevision,
            DateTimeOffset.UtcNow, new(Guid.NewGuid(), id, ResourceOperationType.Remove, installed.Epoch, DateTimeOffset.UtcNow, "{}"));

        await using (var second = File.OpenRead(args[2]))
        {
            var plan = await installer.PlanAsync(second);
            if (plan.ResourceId != id || plan.Version != "1.0.1" || plan.ContentCount != 5 ||
                plan.Update is not { CanApply: true, Added: 1, Unchanged: 4, Changed: 0, Removed: 0 })
                throw new Exception("Unexpected upgrade preview: " + JsonSerializer.Serialize(plan.Update));
            await installer.InstallAsync(plan);
        }
        var updated = (await management.GetAsync()).Single();
        var after = await management.GetEntriesAsync(id);
        if (updated.State.Version != "1.0.1" || updated.Count != 5 || updated.RemovedCount != 1 ||
            !after.Any(e => e.EntryId == withdrawn.EntryId && e.Removed) ||
            !after.Any(e => e.EntryId == "entry-5" && e.Title == "中国" && !e.Removed))
            throw new Exception("Upgrade did not preserve withdrawn state and add entry-5.");
        string? audioHash = null;
        if (args.Length >= 4)
        {
            await using var third = File.OpenRead(args[3]);
            var plan = await installer.PlanAsync(third);
            if (plan.ResourceId != id || plan.Version != "1.0.2" || plan.ContentCount != 5 ||
                plan.Update is not { CanApply: true, Added: 0, Removed: 0 })
                throw new Exception("Unexpected audio upgrade preview: " + JsonSerializer.Serialize(plan.Update));
            await installer.InstallAsync(plan);
            updated = (await management.GetAsync()).Single();
            after = await management.GetEntriesAsync(id);
            if (updated.State.Version != "1.0.2" || updated.Count != 5 || updated.RemovedCount != 1 ||
                !after.Any(e => e.EntryId == withdrawn.EntryId && e.Removed))
                throw new Exception("Audio upgrade changed resource content or withdrawal.");
            var document = JsonSerializer.Deserialize<ContentDocument>(after.Single(e => e.EntryId == withdrawn.EntryId).BodyJson, ContentJson.Options)!;
            var tracks = await new LocalAudioStore(db).ListTracksAsync(document.TextUnits[0].Id);
            var track = tracks.Single();
            if (track.SourceRole != "standard" || !track.Eligible || !track.Preferred || track.DurationMs != 1000)
                throw new Exception("Packaged test tone was not bound as the preferred standard track.");
            await using var playback = await new LocalAudioStore(db).OpenPlaybackAsync("track", track.Id);
            var wave = PcmWave.Inspect(playback.Stream);
            if (wave is not { DurationMs: 1000, SampleRate: 8000, Channels: 1 })
                throw new Exception("Installed audio metadata is invalid.");
            playback.Stream.Position = 0;
            audioHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(playback.Stream));
            using var archive = ZipFile.OpenRead(args[3]);
            var media = archive.Entries.Single(e => e.FullName.EndsWith(".wav", StringComparison.Ordinal));
            await using var source = media.Open();
            if (audioHash != Convert.ToHexStringLower(await SHA256.HashDataAsync(source)))
                throw new Exception("Installed audio bytes changed.");
        }
        string? finalTitle = null;
        if (args.Length == 5)
        {
            await using var fourth = File.OpenRead(args[4]);
            var plan = await installer.PlanAsync(fourth);
            if (plan.ResourceId != id || plan.Version != "1.0.3" || plan.ContentCount != 5 ||
                plan.Update is not { CanApply: true, Added: 0, Removed: 0 } || plan.Update.Changed < 1)
                throw new Exception("Unexpected title upgrade preview: " + JsonSerializer.Serialize(plan.Update));
            await installer.InstallAsync(plan);
            updated = (await management.GetAsync()).Single();
            after = await management.GetEntriesAsync(id);
            finalTitle = after.Single(e => e.EntryId == withdrawn.EntryId).Title;
            if (updated.State.Version != "1.0.3" || updated.Count != 5 || updated.RemovedCount != 1 ||
                finalTitle != "“在”表示位置（工程新版）" ||
                !after.Single(e => e.EntryId == withdrawn.EntryId).Removed)
                throw new Exception("Title upgrade did not preserve the resource and withdrawn entry.");
            var document = JsonSerializer.Deserialize<ContentDocument>(after.Single(e => e.EntryId == withdrawn.EntryId).BodyJson, ContentJson.Options)!;
            var tracks = await new LocalAudioStore(db).ListTracksAsync(document.TextUnits[0].Id);
            if (tracks.Count != 1 || tracks[0] is not { SourceRole: "standard", Eligible: true, Preferred: true })
                throw new Exception("Title upgrade lost its standard test tone.");
        }
        var report = new { status = "PASS", resourceId = id, initialEntries = 4, upgradedEntries = 5,
            finalVersion = updated.State.Version, withdrawnEntry = withdrawn.EntryId, withdrawnPreserved = true,
            addedTitle = "中国", finalTitle, audioSha256 = audioHash, privateDatabase = root };
        await File.WriteAllTextAsync(Path.Combine(root, "verification.json"), JsonSerializer.Serialize(report));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
