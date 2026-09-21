using System.Threading.Channels;
using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Pinyin;

public sealed class PlaybackCoordinatorTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DictionaryRetapCancelsPreparationAndOtherPassageWaitsForCleanup(bool samePassage)
    {
        var player = new PlaybackCoordinator(new SynchronousCancellation()); var owner = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextStarted = false;
        var first = player.PlayOperationAsync(owner, "dictionary-passage:unit:0:12", async token =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.SetResult(); await releaseCleanup.Task; }
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var next = player.PlayOperationAsync(owner, samePassage ? "dictionary-passage:unit:0:12" : "dictionary-passage:unit:13:24",
            _ => { nextStarted = true; return Task.CompletedTask; });
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(nextStarted); Assert.False(next.IsCompleted);
        releaseCleanup.SetResult();
        Assert.Equal(PlaybackOutcome.Cancelled, await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(samePassage ? PlaybackOutcome.Cancelled : PlaybackOutcome.Completed, await next.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(!samePassage, nextStarted);
    }
    [Fact]
    public async Task SpeechOperationCannotOverlapRecordingAndStopAwaitsCleanup()
    {
        var player = new PlaybackCoordinator(new SynchronousCancellation()); var owner = Guid.NewGuid();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var playing = player.PlayOperationAsync(owner, "speech", async token =>
        {
            entered.SetResult(true);
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { await cleanup.Task; }
        });
        await entered.Task;
        var stopping = player.StopAsync(owner); Assert.False(stopping.IsCompleted);
        var recordingEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = player.RecordAsync(Guid.NewGuid(), async token => { recordingEntered.SetResult(true); await Task.Delay(Timeout.Infinite, token); });
        Assert.False(recordingEntered.Task.IsCompleted);
        cleanup.SetResult(true);
        Assert.Equal(PlaybackOutcome.Cancelled, await playing); await stopping; await recordingEntered.Task;
        var invoked = false;
        Assert.Equal(PlaybackOutcome.Busy, await player.PlayOperationAsync(owner, "another", _ => { invoked = true; return Task.CompletedTask; }));
        Assert.False(invoked); await player.StopAsync(); Assert.Equal(PlaybackOutcome.Cancelled, await recording);
    }

    [Fact]
    public async Task SpeechCleanupFailureBlocksNewNativePlayback()
    {
        var player = new PlaybackCoordinator(new SynchronousCancellation()); var owner = Guid.NewGuid();
        Assert.Equal(PlaybackOutcome.Failed, await player.PlayOperationAsync(owner, "speech", _ => throw new PlaybackCleanupException(new IOException())));
        var invoked = false;
        Assert.Equal(PlaybackOutcome.Failed, await player.PlayOperationAsync(owner, "next", _ => { invoked = true; return Task.CompletedTask; }));
        Assert.False(invoked);
    }
    [Fact]
    public async Task SynchronousCancellationStillTogglesOffInsteadOfRestarting()
    {
        var backend = new SynchronousCancellation(); var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var first = player.PlayAsync(owner, "same");
        Assert.Equal(PlaybackOutcome.Cancelled, await player.PlayAsync(owner, "same"));
        Assert.Equal(PlaybackOutcome.Cancelled, await first); Assert.Equal(1, backend.Calls);
    }
    private sealed class SynchronousCancellation : IAudioPlaybackBackend
    {
        public int Calls;
        public async Task PlayAsync(string key, CancellationToken token)
        {
            Calls++; var ended = new TaskCompletionSource();
            using var registration = token.Register(() => ended.TrySetCanceled(token)); await ended.Task;
        }
    }
    [Fact]
    public async Task SequenceStaysInOneSessionAndWaitsForEachNativeCompletion()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend); var progress = new List<int>();
        var task = player.PlaySequenceAsync(Guid.NewGuid(), "whole", _ => Task.FromResult<IReadOnlyList<string>>(["a", "b"]), i => progress.Add(i));
        var a = await Bounded(backend.Started.Reader.ReadAsync().AsTask()); Assert.Equal("a", a.Target);
        Assert.False(player.IsRecording); Assert.False(backend.Started.Reader.TryRead(out _));
        a.End.SetResult(); var b = await Bounded(backend.Started.Reader.ReadAsync().AsTask()); Assert.Equal("b", b.Target);
        b.End.SetResult(); Assert.Equal(PlaybackOutcome.Completed, await Bounded(task)); Assert.Equal(new[] { 0, 1 }, progress);
        Assert.Equal(1, backend.MaxActive);
    }

    [Fact]
    public async Task CancellingPreparationCannotStartALateQueue()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var ready = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = player.PlaySequenceAsync(owner, "loading", _ => ready.Task);
        var stop = player.StopAsync(); ready.SetResult(["a", "b"]); await stop;
        Assert.Equal(PlaybackOutcome.Cancelled, await task); Assert.False(backend.Started.Reader.TryRead(out _));
    }

    [Fact]
    public async Task FailedQueuePreparationPlaysNothingAndReleasesReservation()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend);
        Assert.Equal(PlaybackOutcome.Failed, await player.PlaySequenceAsync(Guid.NewGuid(), "missing", _ => throw new InvalidDataException()));
        Assert.False(backend.Started.Reader.TryRead(out _));
        Assert.Equal(PlaybackOutcome.Completed, await player.RecordAsync(Guid.NewGuid(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task QueueItemFailureStopsRemainingItems()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend);
        var task = player.PlaySequenceAsync(Guid.NewGuid(), "whole", _ => Task.FromResult<IReadOnlyList<string>>(["bad", "never"]));
        var bad = await Bounded(backend.Started.Reader.ReadAsync().AsTask()); bad.End.SetException(new IOException());
        Assert.Equal(PlaybackOutcome.Failed, await Bounded(task)); Assert.False(backend.Started.Reader.TryRead(out _));
    }

    [Fact]
    public async Task QueueReplacementWaitsForCleanupAndNeverResumesOldTail()
    {
        var backend = new Backend { HoldCleanup = true }; var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var task = player.PlaySequenceAsync(owner, "whole", _ => Task.FromResult<IReadOnlyList<string>>(["a", "never"]));
        var a = await Bounded(backend.Started.Reader.ReadAsync().AsTask()); var replacement = player.PlayAsync(owner, "new");
        await Bounded(a.Cancelled.Task); Assert.False(backend.Started.Reader.TryRead(out _)); a.Cleanup.SetResult();
        var next = await Bounded(backend.Started.Reader.ReadAsync().AsTask()); Assert.Equal("new", next.Target);
        a.End.SetResult(); next.End.SetResult(); next.Cleanup.SetResult();
        Assert.Equal(PlaybackOutcome.Cancelled, await task); Assert.Equal(PlaybackOutcome.Completed, await replacement);
        Assert.False(backend.Started.Reader.TryRead(out _)); Assert.Equal(1, backend.MaxActive);
    }

    [Fact]
    public async Task SameQueueToggleAndBackgroundStopCancelAllRemainingItems()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        Task<IReadOnlyList<string>> Prepare(CancellationToken _) => Task.FromResult<IReadOnlyList<string>>(["a", "never"]);
        var first = player.PlaySequenceAsync(owner, "whole", Prepare); await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        Assert.Equal(PlaybackOutcome.Cancelled, await player.PlaySequenceAsync(owner, "whole", Prepare));
        Assert.Equal(PlaybackOutcome.Cancelled, await first); Assert.False(backend.Started.Reader.TryRead(out _));
        var second = player.PlaySequenceAsync(owner, "whole", Prepare); await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        await player.StopAsync(); Assert.Equal(PlaybackOutcome.Cancelled, await second); Assert.False(backend.Started.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RecordingPreemptsQueueButQueueCannotPreemptRecording()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var task = player.PlaySequenceAsync(owner, "whole", _ => Task.FromResult<IReadOnlyList<string>>(["a", "never"]));
        await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        var recording = player.RecordAsync(owner, token => Task.Delay(Timeout.Infinite, token));
        Assert.Equal(PlaybackOutcome.Busy, await player.PlaySequenceAsync(owner, "whole", _ => throw new Exception("Must not prepare")));
        await player.StopAsync(); Assert.Equal(PlaybackOutcome.Cancelled, await task); Assert.Equal(PlaybackOutcome.Cancelled, await recording);
        Assert.False(backend.Started.Reader.TryRead(out _));
    }

    private static async Task<T> Bounded<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(5));
    [Fact]
    public async Task ReplacementWaitsForOldNativeCleanupBeforeStarting()
    {
        var backend = new Backend { HoldCleanup = true }; var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var first = player.PlayAsync(owner, "a"); var a = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        var second = player.PlayAsync(owner, "b"); await Bounded(a.Cancelled.Task);
        Assert.False(backend.Started.Reader.TryRead(out _));
        a.Cleanup.SetResult(); Assert.Equal(PlaybackOutcome.Cancelled, await Bounded(first));
        var b = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        b.End.SetResult(); b.Cleanup.SetResult(); Assert.Equal(PlaybackOutcome.Completed, await Bounded(second));
        Assert.Equal(1, backend.MaxActive);
    }

    [Fact]
    public async Task OwnerScopedStopCannotStopTheNewPage()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var task = player.PlayAsync(owner, "a"); var a = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        await player.StopAsync(Guid.NewGuid()); Assert.False(a.Cancelled.Task.IsCompleted);
        await player.StopAsync(owner); Assert.Equal(PlaybackOutcome.Cancelled, await Bounded(task));
    }

    [Fact]
    public async Task SameTargetTogglesOffAndCanThenBePlayedAgain()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var first = player.PlayAsync(owner, "a"); await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        Assert.Equal(PlaybackOutcome.Cancelled, await Bounded(player.PlayAsync(owner, "a")));
        Assert.Equal(PlaybackOutcome.Cancelled, await Bounded(first));
        var next = player.PlayAsync(owner, "a"); var call = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        call.End.SetResult(); Assert.Equal(PlaybackOutcome.Completed, await Bounded(next));
    }

    [Fact]
    public async Task LateCompletionOfCancelledSessionDoesNotFinishNewSession()
    {
        var backend = new Backend(); var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var first = player.PlayAsync(owner, "a"); var a = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        var second = player.PlayAsync(owner, "b"); var b = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        a.End.SetResult(); Assert.False(second.IsCompleted); Assert.Equal(PlaybackOutcome.Cancelled, await Bounded(first));
        b.End.SetResult(); Assert.Equal(PlaybackOutcome.Completed, await Bounded(second));
    }

    [Fact]
    public async Task BackgroundStopCancelsPendingAndCurrentRequests()
    {
        var backend = new Backend { HoldCleanup = true }; var player = new PlaybackCoordinator(backend);
        var first = player.PlayAsync(Guid.NewGuid(), "a"); var a = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        var pending = player.PlayAsync(Guid.NewGuid(), "b"); var stop = player.StopAsync(); a.Cleanup.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlaybackOutcome.Cancelled, await Bounded(first)); Assert.Equal(PlaybackOutcome.Cancelled, await Bounded(pending));
        Assert.False(backend.Started.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RecordingReservesPermissionAndFinalizationAndNeverGetsCancelledByPlay()
    {
        var backend = new Backend { HoldCleanup = true }; var player = new PlaybackCoordinator(backend); var owner = Guid.NewGuid();
        var play = player.PlayAsync(owner, "a"); var call = await Bounded(backend.Started.Reader.ReadAsync().AsTask());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalize = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = player.RecordAsync(owner, async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { }
            await finalize.Task;
        });
        Assert.True(player.IsRecording); Assert.False(started.Task.IsCompleted);
        Assert.Equal(PlaybackOutcome.Busy, await player.PlayAsync(Guid.NewGuid(), "b"));
        call.Cleanup.SetResult(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await player.StopAsync(Guid.NewGuid()); Assert.False(recording.IsCompleted);
        var stopped = player.StopAsync(owner);
        Assert.Equal(PlaybackOutcome.Busy, await player.RecordAsync(owner, _ => Task.CompletedTask));
        Assert.Equal(PlaybackOutcome.Busy, await player.PlayAsync(owner, "c"));
        Assert.False(stopped.IsCompleted); finalize.SetResult(); await stopped.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlaybackOutcome.Cancelled, await recording); Assert.False(player.IsRecording);
        Assert.Equal(PlaybackOutcome.Cancelled, await play); Assert.Equal(1, backend.MaxActive);
    }

    [Fact]
    public async Task FailedNativeRecordingCleanupBlocksAllFurtherAudio()
    {
        var player = new PlaybackCoordinator(new Backend());
        Assert.Equal(PlaybackOutcome.Failed, await player.RecordAsync(Guid.NewGuid(), _ => throw new PlaybackCleanupException(new IOException())));
        Assert.Equal(PlaybackOutcome.Failed, await player.PlayAsync(Guid.NewGuid(), "a"));
        Assert.Equal(PlaybackOutcome.Failed, await player.RecordAsync(Guid.NewGuid(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task CancelledPermissionDoesNotLeaveRecordingReservation()
    {
        var player = new PlaybackCoordinator(new Backend()); var owner = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = player.RecordAsync(owner, async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        await started.Task; await player.StopAsync(); Assert.Equal(PlaybackOutcome.Cancelled, await recording);
        Assert.Equal(PlaybackOutcome.Completed, await player.RecordAsync(owner, _ => Task.CompletedTask));
    }

    private sealed class Backend : IAudioPlaybackBackend
    {
        public bool HoldCleanup;
        public int Active, MaxActive;
        public Channel<Call> Started { get; } = Channel.CreateUnbounded<Call>();
        public async Task PlayAsync(string target, CancellationToken cancellationToken)
        {
            var call = new Call(target); var active = Interlocked.Increment(ref Active); MaxActive = Math.Max(MaxActive, active);
            Started.Writer.TryWrite(call);
            try { await call.End.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { call.Cancelled.SetResult(true); throw; }
            finally { if (HoldCleanup) await call.Cleanup.Task; Interlocked.Decrement(ref Active); }
        }
    }
    private sealed class Call(string target)
    {
        public string Target { get; } = target;
        public TaskCompletionSource End { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
