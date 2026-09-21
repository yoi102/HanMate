using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Tests.Database;

public sealed partial class BackupTests
{
    private static async Task<Guid> SafetyCopy(Area a)
    {
        using var bytes = await a.Export(); var replacement = new BackupReplacementStore(a.Db);
        var plan = await replacement.PlanAsync(bytes); await replacement.PrepareSafetyAsync(plan); return plan.OperationId;
    }
    private sealed class MaintenanceClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public async Task SafetyRestoreIncludesTrashResourcesMediaAndCanUndoWithNewSnapshot()
    {
        using var a = new Area(); var doc = await a.Seed(); await a.Record(doc); await a.Install();
        var favorites = new FavoriteStore(a.Db); var folder = (await favorites.GetFoldersAsync())[0];
        await favorites.SetSelectionAsync(doc.Id, [folder.Id], (await favorites.GetSelectionAsync(doc.Id)).Revision);
        var trash = new ContentTrashStore(a.Db); await trash.MoveAsync(await trash.PreviewAsync(doc.Id));
        var sourceId = await SafetyCopy(a); var count = await a.Scalar("SELECT count(*) FROM content");
        await trash.RestoreAsync(Assert.Single(await trash.ListAsync()));
        var documents = new SqliteContentDocumentStore(a.Db, new()); var open = (await documents.GetAsync(doc.Id))!;
        await documents.SaveAsync(open.Document with { Title = "after snapshot" }, open.RowRevision);
        var added = await a.Seed();
        var staleEditor = (await documents.GetAsync(doc.Id))!; var epoch = (long)(await a.Scalar("SELECT data_epoch FROM app_state"))!;
        var store = new SafetyRecoveryStore(a.Db); var plan = await store.PlanAsync(sourceId);
        Assert.Equal(1, plan.Trash); await store.PrepareAsync(plan); await store.RestoreAsync(plan);
        Assert.Equal(count, await a.Scalar("SELECT count(*) FROM content")); Assert.Single(await trash.ListAsync());
        Assert.Null(await documents.GetAsync(added.Id)); Assert.True((long)(await a.Scalar("SELECT data_epoch FROM app_state"))! > epoch);
        Assert.Equal(1L, await a.Scalar("SELECT count(*) FROM favorite_item")); Assert.Equal(1L, await a.Scalar("SELECT count(*) FROM installed_resource"));
        Assert.Equal("safetyRestore.v1", await a.Scalar("SELECT json_extract(result_json,'$.format') FROM import_operation"));
        var restoredRevision = (long)(await a.Scalar($"SELECT row_revision FROM content WHERE id='{doc.Id}'"))!;
        Assert.True(restoredRevision > staleEditor.RowRevision);
        await store.RestoreAsync(plan); Assert.Equal(restoredRevision, await a.Scalar($"SELECT row_revision FROM content WHERE id='{doc.Id}'"));
        var rollback = await store.PlanAsync(plan.OperationId); await store.PrepareAsync(rollback); await store.RestoreAsync(rollback);
        Assert.Empty(await trash.ListAsync()); Assert.NotNull(await documents.GetAsync(added.Id));
        Assert.Equal("after snapshot", (await documents.GetAsync(doc.Id))!.Document.Title);
        await Assert.ThrowsAsync<RevisionConflictException>(() => documents.SaveAsync(staleEditor.Document, staleEditor.RowRevision));
        await using var lease = await new LocalAudioStore(a.Db).OpenPlaybackAsync("target", doc.TextUnits[0].Id);
        Assert.Equal(Wave().Length, lease.Stream.Length);
    }
    [Theory]
    [InlineData("stale")][InlineData("damaged")][InlineData("lease")][InlineData("draft")][InlineData("cancel")][InlineData("no-safety")]
    public async Task SafetyRestoreRejectsChangedOrBusyStateWithoutMutation(string fault)
    {
        using var a = new Area(); var doc = await a.Seed(); await a.Record(doc); var id = await SafetyCopy(a);
        var store = new SafetyRecoveryStore(a.Db); var plan = await store.PlanAsync(id);
        if (fault != "no-safety") await store.PrepareAsync(plan);
        if (fault == "stale") await new FavoriteStore(a.Db).SaveFolderAsync("changed", "");
        if (fault == "damaged") await File.AppendAllTextAsync(Path.Combine(a.Root, "recovery", id.ToString("N"), "hanmate.db"), "damage");
        if (fault == "draft") await new TextDraftStore(a.Db).SaveAsync(Guid.NewGuid(), new("draft", "你好", ContentKind.Text), 0);
        if (fault == "lease") await a.Execute($"INSERT INTO file_lease VALUES('{Guid.NewGuid()}','hash','export','{DateTime.UtcNow.AddHours(1):O}')");
        var before = await a.Scalar("SELECT data_epoch FROM app_state");
        using var cancellation = new CancellationTokenSource(); if (fault == "cancel") cancellation.Cancel();
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RestoreAsync(plan, cancellation.Token));
        else await Assert.ThrowsAsync<PackageException>(() => store.RestoreAsync(plan));
        Assert.Equal(before, await a.Scalar("SELECT data_epoch FROM app_state")); Assert.Equal(0L, await a.Scalar("SELECT count(*) FROM import_operation"));
        Assert.NotNull(await new SqliteContentDocumentStore(a.Db, new()).GetAsync(doc.Id));
    }
    [Fact]
    public async Task SafetyRestoreSqlFailureRollsBackAndRetryCommitsOnce()
    {
        using var a = new Area(); await a.Seed(); var id = await SafetyCopy(a); var added = await a.Seed();
        var store = new SafetyRecoveryStore(a.Db); var plan = await store.PlanAsync(id); await store.PrepareAsync(plan);
        await a.Execute("CREATE TRIGGER fail_restore BEFORE INSERT ON import_operation BEGIN SELECT RAISE(ABORT,'injected'); END");
        await Assert.ThrowsAsync<SqliteException>(() => store.RestoreAsync(plan));
        Assert.NotNull(await new SqliteContentDocumentStore(a.Db, new()).GetAsync(added.Id));
        await a.Execute("DROP TRIGGER fail_restore"); await store.RestoreAsync(plan);
        Assert.Null(await new SqliteContentDocumentStore(a.Db, new()).GetAsync(added.Id));
    }
    [Fact]
    public async Task SafetySnapshotIncludesWalPagesAndReinstallsMissingUnreferencedMedia()
    {
        using var a = new Area(); var doc = await a.Seed(); await a.Execute("PRAGMA journal_mode=WAL"); await a.Record(doc);
        var relative = (string)(await a.Scalar("SELECT relative_path FROM audio_asset"))!;
        var source = await SafetyCopy(a);
        await a.Execute("DELETE FROM audio_binding; DELETE FROM audio_asset"); File.Delete(Path.Combine(a.Root, relative));
        var store = new SafetyRecoveryStore(a.Db); var plan = await store.PlanAsync(source); await store.PrepareAsync(plan); await store.RestoreAsync(plan);
        Assert.Equal(Wave(), await File.ReadAllBytesAsync(Path.Combine(a.Root, relative)));
        await using var lease = await new LocalAudioStore(a.Db).OpenPlaybackAsync("target", doc.TextUnits[0].Id); Assert.Equal(Wave().Length, lease.Stream.Length);
    }
    [Fact]
    public async Task SafetyListRejectsFutureSchemaAndRetentionNeverPrunesTheSoleValidCopy()
    {
        using var a = new Area(); await a.Seed(); var good = await SafetyCopy(a); var future = await SafetyCopy(a);
        var directory = Path.Combine(a.Root, "recovery", future.ToString("N"));
        using (var c = new SqliteConnection($"Data Source={Path.Combine(directory, "hanmate.db")};Pooling=False"))
        { await c.OpenAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA user_version=999"; await cmd.ExecuteNonQueryAsync(); }
        var ready = Path.Combine(directory, "ready.json");
        var manifest = System.Text.Json.JsonSerializer.Deserialize<SafetyManifest>(await File.ReadAllBytesAsync(ready))!;
        manifest.Files["hanmate.db"] = await SafetyArchive.HashAsync(Path.Combine(directory, "hanmate.db"), default);
        await SafetyArchive.WriteJsonAsync(ready, manifest, default);
        var store = new SafetyRecoveryStore(a.Db);
        Assert.Contains(await store.ListAsync(), x => x.Id == future && x.Status == "incompatible");
        Assert.Equal("BACKUP_INCOMPATIBLE", (await Assert.ThrowsAsync<PackageException>(() => store.PlanAsync(future))).Code);
        var retention = await store.PlanRetentionAsync(); Assert.Empty(retention.Items); await store.RetainRecentAsync(retention);
        Assert.Contains(await store.ListAsync(), x => x.Id == good && x.Status == "valid");
    }
    [Fact]
    public async Task SafetyRetentionKeepsLatestThreeAndDamagedCopies()
    {
        using var a = new Area(); await a.Seed(); var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var id = await SafetyCopy(a); ids.Add(id);
            File.SetLastWriteTimeUtc(Path.Combine(a.Root, "recovery", id.ToString("N"), "ready.json"), DateTime.UtcNow.AddDays(i - 10));
        }
        await File.AppendAllTextAsync(Path.Combine(a.Root, "recovery", ids[0].ToString("N"), "hanmate.db"), "damaged");
        var store = new SafetyRecoveryStore(a.Db); var plan = await store.PlanRetentionAsync(); Assert.Equal(2, plan.Items.Count);
        await store.RetainRecentAsync(plan);
        await store.RetainRecentAsync(plan); // Ambiguous completion is safe to retry.
        var remaining = await store.ListAsync(); Assert.Equal(4, remaining.Count); Assert.Contains(remaining, x => x.Id == ids[0] && x.Status == "damaged");
        Assert.All(ids.TakeLast(3), id => Assert.Contains(remaining, x => x.Id == id && x.Status == "valid"));
        Assert.Empty((await store.PlanRetentionAsync()).Items);
    }
    [Fact]
    public async Task OrphanScanWaitsSevenDaysAndDeletesOnlyOwnedUnreferencedFiles()
    {
        using var a = new Area(); var doc = await a.Seed(); await a.Record(doc);
        var audio = new LocalAudioStore(a.Db); var draft = await audio.ImportAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id), new MemoryStream(Wave()));
        await audio.DiscardAsync(draft.Id); var orphan = Path.Combine(a.Root, "audio", "local", draft.Id.ToString("D") + ".wav");
        var unknown = Path.Combine(Path.GetDirectoryName(orphan)!, "user-notes.wav"); await File.WriteAllTextAsync(unknown, "keep");
        var clock = new MaintenanceClock(); var store = new OrphanAudioStore(a.Db) { Clock = clock };
        var scan = await store.ScanAsync(); Assert.False(Assert.Single(scan.Items).Eligible); Assert.Equal(0, (await store.CleanAsync(scan)).Deleted);
        clock.Now += TimeSpan.FromDays(8); scan = await store.ScanAsync(); Assert.True(Assert.Single(scan.Items).Eligible);
        var result = await store.CleanAsync(scan); Assert.Equal(1, result.Deleted); Assert.False(File.Exists(orphan)); Assert.True(File.Exists(unknown));
        Assert.Equal(0, (await store.CleanAsync(scan)).Deleted);
        await using var lease = await audio.OpenPlaybackAsync("target", doc.TextUnits[0].Id); Assert.Equal(Wave().Length, lease.Stream.Length);
    }
    [Theory]
    [InlineData("binding")][InlineData("lease")][InlineData("draft")][InlineData("snapshot")][InlineData("damaged-snapshot")][InlineData("changed-bytes")]
    public async Task OrphanCleanupRechecksAllReferencesAndFileIdentity(string protection)
    {
        using var a = new Area(); var doc = await a.Seed(); await a.Record(doc);
        var binding = (string)(await a.Scalar("SELECT id FROM audio_binding"))!;
        var asset = (string)(await a.Scalar("SELECT asset_id FROM audio_binding"))!;
        var path = Path.Combine(a.Root, (string)(await a.Scalar("SELECT relative_path FROM audio_asset"))!);
        await a.Execute("DELETE FROM audio_binding");
        var clock = new MaintenanceClock(); var store = new OrphanAudioStore(a.Db) { Clock = clock };
        await store.ScanAsync(); clock.Now += TimeSpan.FromDays(8); var plan = await store.ScanAsync(); Assert.True(Assert.Single(plan.Items).Eligible);
        if (protection == "binding") await a.Execute($"INSERT INTO audio_binding SELECT '{binding}',id,'{asset}',text_hash,pronunciation_hash,'confirmed','user','' FROM playback_target LIMIT 1");
        if (protection == "lease") await a.Execute($"INSERT INTO file_lease VALUES('{Guid.NewGuid()}','hash','export','{DateTime.UtcNow.AddHours(1):O}')");
        if (protection == "draft") await new TextDraftStore(a.Db).SaveAsync(Guid.NewGuid(), new("draft", "你好", ContentKind.Text), 0);
        if (protection is "snapshot" or "damaged-snapshot")
        {
            var id = await SafetyCopy(a);
            if (protection == "damaged-snapshot") await File.AppendAllTextAsync(Path.Combine(a.Root, "recovery", id.ToString("N"), "hanmate.db"), "damage");
        }
        if (protection == "changed-bytes") await File.AppendAllTextAsync(path, "changed");
        Assert.Equal(0, (await store.CleanAsync(plan)).Deleted); Assert.True(File.Exists(path));
        Assert.Equal(1L, await a.Scalar("SELECT count(*) FROM audio_asset"));
    }
    [Fact]
    public async Task OrphanCleanupInterruptedAfterMetadataLeavesBytesAndRetryIsSafe()
    {
        using var a = new Area(); var doc = await a.Seed(); await a.Record(doc); await a.Execute("DELETE FROM audio_binding");
        var clock = new MaintenanceClock(); var normal = new OrphanAudioStore(a.Db) { Clock = clock };
        await normal.ScanAsync(); clock.Now += TimeSpan.FromDays(8); var plan = await normal.ScanAsync();
        var failing = new OrphanAudioStore(a.Db) { Clock = clock, Checkpoint = _ => throw new InvalidOperationException("interruption") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CleanAsync(plan));
        Assert.Equal(0L, await a.Scalar("SELECT count(*) FROM audio_asset")); Assert.True(File.Exists(Path.Combine(a.Root, plan.Items[0].RelativePath)));
        Assert.Equal(1, (await normal.CleanAsync(plan)).Deleted); Assert.Equal("ok", await a.Scalar("PRAGMA integrity_check"));
    }
    [Fact]
    public async Task OrphanCleanupProtectsReferenceInsertedBetweenMetadataCommitAndDelete()
    {
        using var a = new Area(); var doc = await a.Seed(); await a.Record(doc);
        await a.Execute("CREATE TABLE saved_asset AS SELECT * FROM audio_asset; CREATE TABLE saved_binding AS SELECT * FROM audio_binding; DELETE FROM audio_binding");
        var clock = new MaintenanceClock(); var scan = new OrphanAudioStore(a.Db) { Clock = clock };
        await scan.ScanAsync(); clock.Now += TimeSpan.FromDays(8); var plan = await scan.ScanAsync();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan.CleanAsync(plan, cancel.Token));
        var store = new OrphanAudioStore(a.Db)
        {
            Clock = clock, Checkpoint = stage =>
            {
                if (stage == "gc-after-metadata") a.Execute("INSERT INTO audio_asset SELECT * FROM saved_asset; INSERT INTO audio_binding SELECT * FROM saved_binding").GetAwaiter().GetResult();
            }
        };
        var result = await store.CleanAsync(plan); Assert.Equal(0, result.Deleted); Assert.Equal(1, result.Skipped);
        await using var lease = await new LocalAudioStore(a.Db).OpenPlaybackAsync("target", doc.TextUnits[0].Id); Assert.Equal(Wave().Length, lease.Stream.Length);
    }
}
