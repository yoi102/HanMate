using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class LocalAudioTests
{
    [Fact]
    public async Task RecordingExportCopiesActualBytesUnderLeaseWithoutTextAndReleasesOnFailure()
    {
        using var area = new Area(); var (store, target) = await Seed(area); using var wave = Wave();
        var draft = await store.BeginAsync(target, "user"); await File.WriteAllBytesAsync(store.CapturePath(draft.Id), wave.ToArray());
        draft = await store.FinalizeAsync(draft.Id); var binding = await store.SaveAsync(draft, "recording", false);
        using var output = new MemoryStream(); await store.ExportRecordingAsync(target, binding, output);
        Assert.Equal(wave.ToArray(), output.ToArray()); Assert.Equal(0, await Scalar(area, "SELECT count(*) FROM file_lease"));
        using var closed = new MemoryStream(); closed.Close(); await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ExportRecordingAsync(target, binding, closed));
        Assert.Equal(0, await Scalar(area, "SELECT count(*) FROM file_lease")); Assert.Single(await store.ListTracksAsync(target.Id));
        await ChangePronunciation(area, target); var current = await store.GetTargetAsync(target.Id);
        using var reviewed = new MemoryStream(); await store.ExportRecordingAsync(current, binding, reviewed); Assert.Equal(wave.ToArray(), reviewed.ToArray());
        using var imported = Wave(); var foreign = await store.SaveAsync(await store.ImportAsync(current, imported), "import", false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExportRecordingAsync(current, foreign, reviewed));
    }
    [Fact]
    public async Task ImportSaveReopenAndPlaybackLeasePersistWithoutLosingDraftOnFailure()
    {
        using var area = new Area(); var (store, target) = await Seed(area);
        using var wave = Wave(); var draft = await store.ImportAsync(target, wave);
        Assert.Equal("ready", draft.Phase); Assert.Single(await store.ListDraftsAsync(target.Id));
        var reopened = new LocalAudioStore(area.Db);
        var binding = await reopened.SaveAsync(draft, "voice", true);
        Assert.Empty(await store.ListDraftsAsync(target.Id));
        var track = Assert.Single(await store.ListTracksAsync(target.Id)); Assert.Equal(binding, track.Id); Assert.True(track.Eligible && track.Preferred);
        await using (var lease = await store.OpenPlaybackAsync("target", target.Id))
        { Assert.Equal(1000, PcmWave.Inspect(lease.Stream).DurationMs); Assert.Equal(1, await Scalar(area, "SELECT COUNT(*) FROM file_lease")); }
        Assert.Equal(0, await Scalar(area, "SELECT COUNT(*) FROM file_lease"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(draft, "duplicate", false));
        Assert.Equal(1, await Scalar(area, "SELECT COUNT(*) FROM audio_binding"));
    }

    [Fact]
    public async Task ChangedPronunciationRevokesDefaultButPermitsExplicitAuditionAndReview()
    {
        using var area = new Area(); var (store, target) = await Seed(area);
        using var wave = Wave(); var binding = await store.SaveAsync(await store.ImportAsync(target, wave), "voice", true);
        await ChangePronunciation(area, target);
        Assert.False(Assert.Single(await store.ListTracksAsync(target.Id)).Eligible);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenPlaybackAsync("target", target.Id));
        await using (var audition = await store.OpenPlaybackAsync("track", binding)) Assert.Equal(1000, audition.DurationMs);
        var current = await store.GetTargetAsync(target.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateTrackAsync(current, binding, "default"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateTrackAsync(target, binding, "confirm"));
        await store.UpdateTrackAsync(current, binding, "confirm");
        await using var applicable = await store.OpenPlaybackAsync("target", target.Id); Assert.Equal(1000, applicable.DurationMs);
    }

    [Fact]
    public async Task StaleDraftCommitRollsBackAndNeedsExplicitReviewOfCurrentTarget()
    {
        using var area = new Area(); var (store, target) = await Seed(area); using var wave = Wave();
        var draft = await store.ImportAsync(target, wave); await ChangePronunciation(area, target);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(draft, "old", true));
        Assert.Single(await store.ListDraftsAsync(target.Id)); Assert.Equal(0, await Scalar(area, "SELECT COUNT(*) FROM audio_asset"));
        var current = await store.GetTargetAsync(target.Id); await store.ReviewDraftAsync(draft, current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(draft, "stale", true));
        await store.SaveAsync(Assert.Single(await store.ListDraftsAsync(target.Id)), "reviewed", true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoversCrashBeforeOrAfterFileRename(bool renamed)
    {
        using var area = new Area(); var (store, target) = await Seed(area);
        var pending = await store.BeginAsync(target, "user"); using var wave = Wave();
        await File.WriteAllBytesAsync(store.CapturePath(pending.Id), wave.ToArray());
        if (renamed) File.Move(store.CapturePath(pending.Id), Path.ChangeExtension(store.CapturePath(pending.Id), ".wav"));
        var recovered = await new LocalAudioStore(area.Db).FinalizeAsync(pending.Id);
        Assert.Equal("ready", recovered.Phase); await store.SaveAsync(recovered, "recovered", false);
    }

    [Fact]
    public async Task InterruptedCopyAndInvalidHeadersCannotBecomeAssetsAndRemainDiscardable()
    {
        using var area = new Area(); var (store, target) = await Seed(area);
        using var bad = new MemoryStream("not an audio file"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(target, bad));
        var pending = Assert.Single(await store.ListDraftsAsync(target.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.FinalizeAsync(pending.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(pending, "invalid", false));
        await store.DiscardAsync(pending.Id); Assert.Empty(await store.ListDraftsAsync(target.Id));
        Assert.Equal(0, await Scalar(area, "SELECT COUNT(*) FROM audio_asset"));
    }

    [Fact]
    public async Task ResourceUserTrackRequiresExplicitDefaultAndWholeUnitDoesNotServeSegment()
    {
        using var area = new Area(); var (store, original) = await Seed(area);
        await Execute(area, "UPDATE content SET origin='resource'"); var target = await store.GetTargetAsync(original.Id);
        using var wave = Wave(); var binding = await store.SaveAsync(await store.ImportAsync(target, wave), "practice", false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenPlaybackAsync("target", target.Id));
        await store.UpdateTrackAsync(target, binding, "default"); await using var playback = await store.OpenPlaybackAsync("target", target.Id);
        var segmentId = await ScalarText(area, "SELECT id FROM playback_target WHERE role='segment' LIMIT 1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenPlaybackAsync("target", Guid.Parse(segmentId)));
    }

    [Fact]
    public async Task RemovingTrackPreservesAssetsAndActiveLeaseButRemovesItsPreference()
    {
        using var area = new Area(); var (store, target) = await Seed(area); using var wave = Wave();
        var binding = await store.SaveAsync(await store.ImportAsync(target, wave), "practice", true);
        await using var lease = await store.OpenPlaybackAsync("track", binding);
        await store.UpdateTrackAsync(target, binding, "remove");
        Assert.Empty(await store.ListTracksAsync(target.Id)); Assert.Equal(0, await Scalar(area, "SELECT COUNT(*) FROM audio_preference"));
        Assert.Equal(1, await Scalar(area, "SELECT COUNT(*) FROM audio_asset")); Assert.Equal(1, await Scalar(area, "SELECT COUNT(*) FROM file_lease"));
        Assert.Equal(1000, PcmWave.Inspect(lease.Stream).DurationMs);
    }

    [Fact]
    public async Task TamperedAudioCannotSaveOrPlayAndFailedOpenReleasesLease()
    {
        using var area = new Area(); var (store, target) = await Seed(area); using var wave = Wave(); var draft = await store.ImportAsync(target, wave);
        var path = Path.ChangeExtension(store.CapturePath(draft.Id), ".wav"); var bytes = await File.ReadAllBytesAsync(path); bytes[^1] ^= 1; await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(draft, "tampered", true));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenPlaybackAsync("draft", draft.Id));
        Assert.Equal(0, await Scalar(area, "SELECT COUNT(*) FROM file_lease")); Assert.Single(await store.ListDraftsAsync(target.Id));
    }

    [Fact]
    public async Task ImportLimitIsEnforcedWithoutTrustingLengthOrExtension()
    {
        using var area = new Area(); var (store, target) = await Seed(area); using var input = new EndlessStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(target, input));
        var draft = Assert.Single(await store.ListDraftsAsync(target.Id));
        Assert.True(new FileInfo(store.CapturePath(draft.Id)).Length <= PcmWave.MaximumBytes);
        Assert.Equal("pending", draft.Phase);
    }

    private static async Task<(LocalAudioStore, AudioTarget)> Seed(Area area)
    {
        var body = new TextDraft("Audio test", "你好。世界。", ContentKind.Text, "textDraft.v2");
        body = body with { Annotation = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document };
        var saved = await new EditorCommitStore(area.Db).CommitAsync(await new TextDraftStore(area.Db).SaveAsync(Guid.NewGuid(), body, 0));
        var store = new LocalAudioStore(area.Db); return (store, await store.GetTargetAsync(saved.Document.TextUnits[0].Id));
    }
    private static async Task ChangePronunciation(Area area, AudioTarget target)
    {
        var editor = new EditorCommitStore(area.Db); var original = (await new SqliteContentDocumentStore(area.Db, new()).GetAsync(target.ContentId))!;
        var draft = await editor.StartAsync(original); var token = draft.Body.Annotation!.TextUnits[0].Tokens[0];
        var changed = draft.Body with { Annotation = DraftAnnotation.Correct(draft.Body.Annotation, token.Id, "ni4") };
        await editor.CommitAsync(await new TextDraftStore(area.Db).SaveAsync(draft.Id, changed, draft.Revision));
    }
    private static MemoryStream Wave()
    { var stream = new MemoryStream(); PcmWave.WriteHeader(stream, 8000, 1, 16000); stream.Write(new byte[16000]); stream.Position = 0; return stream; }
    private static async Task<long> Scalar(Area area, string sql) => long.Parse(await ScalarText(area, sql));
    private static async Task<string> ScalarText(Area area, string sql)
    { await using var c = await area.Db.OpenConnectionAsync(); await using var command = c.CreateCommand(); command.CommandText = sql; return (await command.ExecuteScalarAsync())!.ToString()!; }
    private static async Task Execute(Area area, string sql)
    { await using var c = await area.Db.OpenConnectionAsync(); await using var command = c.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private sealed class Area : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "HanMateAudioTests", Guid.NewGuid().ToString("N"));
        public HanMateDatabase Db { get; }
        public Area() { Directory.CreateDirectory(_path); Db = new(Path.Combine(_path, "test.db")); }
        public void Dispose() { Directory.Delete(_path, true); }
    }
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { buffer.Span.Clear(); return ValueTask.FromResult(buffer.Length); }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
