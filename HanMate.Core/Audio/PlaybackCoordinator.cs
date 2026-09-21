namespace HanMate.Core.Audio;

public enum PlaybackOutcome { Completed, Cancelled, Failed, Busy }
public sealed class PlaybackCleanupException(Exception inner) : Exception("Native audio cleanup failed.", inner);
public interface IAudioPlaybackBackend
{
    // Must stop and release native playback before this task completes, including cancellation.
    Task PlayAsync(string assetKey, CancellationToken cancellationToken);
}

/// <summary>New requests cancel and await the old session. Owner-scoped stops cannot cancel another page.</summary>
public sealed class PlaybackCoordinator(IAudioPlaybackBackend backend)
{
    private readonly object _sync = new();
    private Session? _session;
    private bool _faulted;
    public bool IsRecording { get { lock (_sync) return _session?.Recording is not null; } }
    private sealed record Session(Guid Owner, string Target, CancellationTokenSource Cancellation, TaskCompletionSource<PlaybackOutcome> Completion,
        Func<CancellationToken, Task>? Recording = null, Func<CancellationToken, Task>? Playback = null);

    // Reservation covers permission, capture and finalization. Playback must never discard a capture.
    public Task<PlaybackOutcome> RecordAsync(Guid owner, Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_sync)
        {
            if (_faulted) return Task.FromResult(PlaybackOutcome.Failed);
            if (_session?.Recording is not null) return Task.FromResult(PlaybackOutcome.Busy);
            var previous = _session;
            previous?.Cancellation.Cancel();
            var session = new Session(owner, "recording", new(), new(TaskCreationOptions.RunContinuationsAsynchronously), operation);
            _session = session;
            _ = RunAsync(session, previous?.Completion.Task);
            return session.Completion.Task;
        }
    }

    public Task<PlaybackOutcome> PlayAsync(Guid owner, string target) => StartPlayback(owner, target, null);

    /// <summary>Platform speech shares the same reservation, cancellation and native cleanup boundary.</summary>
    public Task<PlaybackOutcome> PlayOperationAsync(Guid owner, string identity, Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return StartPlayback(owner, identity, operation);
    }

    /// <summary>Preparation and every queue item belong to one cancellable session, including gaps between items.</summary>
    public Task<PlaybackOutcome> PlaySequenceAsync(Guid owner, string identity,
        Func<CancellationToken, Task<IReadOnlyList<string>>> prepare, Action<int>? starting = null)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        return StartPlayback(owner, identity, async token =>
        {
            var items = (await prepare(token).ConfigureAwait(false)).ToArray();
            if (items.Length == 0) throw new InvalidOperationException("Empty playback queue.");
            for (var i = 0; i < items.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                starting?.Invoke(i);
                token.ThrowIfCancellationRequested();
                await backend.PlayAsync(items[i], token).ConfigureAwait(false);
            }
        });
    }

    private Task<PlaybackOutcome> StartPlayback(Guid owner, string target, Func<CancellationToken, Task>? operation)
    {
        lock (_sync)
        {
            if (_faulted) return Task.FromResult(PlaybackOutcome.Failed);
            if (_session?.Recording is not null) return Task.FromResult(PlaybackOutcome.Busy);
            var previous = _session;
            var toggle = previous is not null && previous.Owner == owner && previous.Target == target && !previous.Completion.Task.IsCompleted;
            previous?.Cancellation.Cancel();
            // Tapping the currently active target toggles it off, including during loading.
            if (toggle) return previous!.Completion.Task;
            var session = new Session(owner, target, new(), new(TaskCreationOptions.RunContinuationsAsynchronously), Playback: operation);
            _session = session;
            _ = RunAsync(session, previous?.Completion.Task);
            return session.Completion.Task;
        }
    }

    public Task StopAsync(Guid? owner = null)
    {
        lock (_sync)
        {
            var session = _session;
            if (session is null || (owner is not null && session.Owner != owner)) return Task.CompletedTask;
            // Cancellation can finish a synchronous backend and clear _session reentrantly.
            session.Cancellation.Cancel();
            return session.Completion.Task;
        }
    }

    private async Task RunAsync(Session session, Task<PlaybackOutcome>? previous)
    {
        var outcome = PlaybackOutcome.Failed;
        try
        {
            if (previous is not null) await previous.ConfigureAwait(false);
            session.Cancellation.Token.ThrowIfCancellationRequested();
            lock (_sync) { if (_faulted) throw new InvalidOperationException("Previous player did not stop reliably."); }
            if (session.Recording is { } recording) await recording(session.Cancellation.Token).ConfigureAwait(false);
            else if (session.Playback is { } playback) await playback(session.Cancellation.Token).ConfigureAwait(false);
            else await backend.PlayAsync(session.Target, session.Cancellation.Token).ConfigureAwait(false);
            outcome = session.Cancellation.IsCancellationRequested ? PlaybackOutcome.Cancelled : PlaybackOutcome.Completed;
        }
        catch (OperationCanceledException) { outcome = PlaybackOutcome.Cancelled; }
        catch (PlaybackCleanupException) { lock (_sync) _faulted = true; }
        catch { outcome = PlaybackOutcome.Failed; }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_session, session)) _session = null;
                session.Completion.TrySetResult(outcome);
                session.Cancellation.Dispose();
            }
        }
    }
}
