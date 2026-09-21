using HanMate.Core.Audio;

namespace HanMate.App.Audio;

public sealed class PinyinSpeechService(AiVoiceService voice, PlaybackCoordinator playback, IAudioPlaybackBackend backend)
{
    // Reset the old AI-first teaching default independently of the dictionary voice.
    // Users can still explicitly opt into synthesis from Pinyin Resources.
    public const string PreferenceKey = "pinyin.prefer-ai.v2";
    public async Task<PlaybackOutcome?> PlayAsync(Guid owner, string identity, Func<string> phonemes, string? recording)
    {
        // Join the shared audio operation before asynchronous selection/preparation, so
        // leaving the page or tapping another item can cancel this request as well.
        var missing = false;
        var result = await playback.PlayOperationAsync(owner, "pinyin:" + identity, async token =>
        {
            if (recording?.StartsWith("local:", StringComparison.Ordinal) == true)
                await backend.PlayAsync(recording, token);
            else if (recording is not null && (!Preferences.Default.Get(PreferenceKey, false) ||
                PinyinRecordingPolicy.PreferRecording(phonemes(), recording, preferAi: true)))
                await backend.PlayAsync(recording, token);
            else if (await voice.SelectedAsync(token, SpeechEngine.Melo) is { } speaker)
            {
                token.ThrowIfCancellationRequested();
                await voice.SpeakPinyinAsync(phonemes(), speaker, token);
            }
            else if (recording is not null)
            {
                await backend.PlayAsync(recording, token);
            }
            else missing = true;
        });
        return missing && result == PlaybackOutcome.Completed ? null : result;
    }
}
