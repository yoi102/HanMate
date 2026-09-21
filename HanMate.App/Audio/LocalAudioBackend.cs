using HanMate.Core.Audio;
using HanMate.Infrastructure.Database;
using Plugin.Maui.Audio;

namespace HanMate.App.Audio;

public sealed class LocalAudioBackend(BundledPronunciationService bundled, LocalAudioStore store, ReadingAudioStore reading, SpeechPolicyStore speechPolicy, AiVoiceService ai, WordSpeechService words) : IAudioPlaybackBackend
{
    public async Task PlayAsync(string assetKey, CancellationToken cancellationToken)
    {
        if (assetKey.StartsWith("recorded-word:", StringComparison.Ordinal))
        {
            var separator = assetKey.IndexOf(':', "recorded-word:".Length);
            if (separator < 0) throw new InvalidDataException("Invalid recorded headword identity.");
            var catalog = await words.GetCatalogAsync().WaitAsync(cancellationToken);
            var asset = catalog.GetAsset(assetKey["recorded-word:".Length..separator]);
            // The queue retains both text and pronunciation hashes, including user corrections.
            await reading.ReadSpeechAsync(assetKey[(separator + 1)..], cancellationToken);
            await BundledPronunciationService.PlayAssetAsync(asset, cancellationToken);
            return;
        }
        if (assetKey.StartsWith("ai:", StringComparison.Ordinal))
        {
            var separator = assetKey.IndexOf(':', 3);
            if (separator < 0 || !Enum.TryParse<SpeechEngine>(assetKey[3..separator], out var engine) || engine == SpeechEngine.System) throw new InvalidDataException("Invalid AI provider.");
            var speakerEnd = assetKey.IndexOf(':', separator + 1);
            if (speakerEnd < 0 || !int.TryParse(assetKey[(separator + 1)..speakerEnd], out var speaker)) throw new InvalidDataException("Invalid AI voice.");
            var key = assetKey[(speakerEnd + 1)..]; var text = await reading.ReadSpeechAsync(key, cancellationToken);
            foreach (var chunk in OfflineVoiceInput.Chunks(text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SpeechPreferences.Engine != engine || await ai.SelectedAsync(cancellationToken, engine) != speaker || await reading.ReadSpeechAsync(key, cancellationToken) != text)
                    throw new InvalidOperationException("Voice selection or reading target changed.");
                if (!string.IsNullOrWhiteSpace(chunk)) await ai.SpeakAsync(chunk, speaker, cancellationToken, engine);
            }
            return;
        }
        if (assetKey.StartsWith("tts:", StringComparison.Ordinal))
        {
            if (SpeechPreferences.Engine != SpeechEngine.System && !await speechPolicy.IsAllowedAsync(cancellationToken)) throw new InvalidOperationException("System speech is disabled.");
            var separator = assetKey.IndexOf(':', 4); if (separator < 0) throw new InvalidDataException("Invalid voice identity.");
            var voiceId = Uri.UnescapeDataString(assetKey[4..separator]); var key = assetKey[(separator + 1)..];
            var text = await reading.ReadSpeechAsync(key, cancellationToken);
            var voice = (await NativeSpeech.ListAsync(cancellationToken)).Single(v => v.Id == voiceId);
            foreach (var chunk in SpeechText.Split(text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SpeechPreferences.Engine != SpeechEngine.System && !await speechPolicy.IsAllowedAsync(cancellationToken)) throw new InvalidOperationException("System speech is disabled.");
                if (await reading.ReadSpeechAsync(key, cancellationToken) != text) throw new InvalidOperationException("Speech text changed.");
                if (!string.IsNullOrWhiteSpace(chunk)) await NativeSpeech.SpeakAsync(voice, chunk, cancellationToken);
            }
            return;
        }
        if (!assetKey.StartsWith("local:", StringComparison.Ordinal)) { await bundled.PlayAsync(assetKey, cancellationToken); return; }
        var parts = assetKey.Split(':');
        if (parts.Length == 6 && parts[1] == "checked" && Guid.TryParse(parts[2], out var target) && Guid.TryParse(parts[3], out var content))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var checkedAudio = await store.OpenExpectedPlaybackAsync(target, content, parts[4], parts[5]);
            await PlayLeaseAsync(checkedAudio, cancellationToken); return;
        }
        if (parts.Length != 3 || !Guid.TryParse(parts[2], out var id)) throw new InvalidDataException("Invalid audio identity.");
        cancellationToken.ThrowIfCancellationRequested();
        await using var audio = await store.OpenPlaybackAsync(parts[1], id);
        await PlayLeaseAsync(audio, cancellationToken);
    }

    private static async Task PlayLeaseAsync(AudioReadLease audio, CancellationToken cancellationToken)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var player = AudioManager.Current.CreateAsyncPlayer(audio.Stream);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(audio.DurationMs + 8000));
            try { await player.PlayAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("Audio playback timed out."); }
            finally
            {
                try { try { player.Stop(); } finally { player.Dispose(); } }
                catch (Exception error) { throw new PlaybackCleanupException(error); }
            }
        });
    }
}
