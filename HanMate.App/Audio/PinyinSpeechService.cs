using HanMate.Core.Audio;
using HanMate.Core.Content;

namespace HanMate.App.Audio;

public sealed class PinyinSpeechService(WordSpeechService words, TextSpeechService speech, PlaybackCoordinator playback, IAudioPlaybackBackend backend)
{
    public Task<PlaybackOutcome?> PlayExampleAsync(Guid owner, TextUnit unit, string? recording) =>
        PlayAsync(owner, unit.Id.ToString(), async token =>
        {
            // Course keys already validate the complete word reading; imported recordings stay first.
            if (recording is not null)
                await backend.PlayAsync(recording, token);
            else
                await words.SpeakAsync(unit.Text, unit.Tokens.Select(t => t.Pinyin).ToArray(),
                    () => PinyinVoiceInput.Example(unit), token);
        });

    public Task<PlaybackOutcome?> PlayDemoAsync(Guid owner, string identity, Func<string> phonemes, string? recording) =>
        PlayAsync(owner, identity, async token =>
        {
            if (recording is not null)
                await backend.PlayAsync(recording, token);
            else if (SpeechPreferences.Engine == SpeechEngine.Melo)
                await speech.SpeakAsync("", phonemes, token);
            else
                // Text-only engines cannot pronounce an isolated final such as ong from Chinese text.
                // Never silently switch providers or read its Latin letters as a teaching demonstration.
                throw new SpeechUnavailableException(SpeechPreferences.Engine == SpeechEngine.System);
        });

    private async Task<PlaybackOutcome?> PlayAsync(Guid owner, string identity, Func<CancellationToken, Task> operation)
    {
        // Join the shared audio operation before asynchronous selection/preparation, so
        // leaving the page or tapping another item can cancel this request as well.
        var missing = false;
        var result = await playback.PlayOperationAsync(owner, "pinyin:" + identity, async token =>
        {
            try { await operation(token); }
            catch (SpeechUnavailableException) { missing = true; }
        });
        return missing && result == PlaybackOutcome.Completed ? null : result;
    }
}
