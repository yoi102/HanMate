using HanMate.Core.Audio;
using HanMate.Infrastructure.Voices;

namespace HanMate.App.Audio;

public sealed class AiVoiceService(SpeechVoicePacks packs)
{
    private OfflineVoiceSynthesizer Synth(SpeechEngine engine) => new(packs.Get(engine));
    public async Task<int?> SelectedAsync(CancellationToken token, SpeechEngine? engine = null) =>
        OfflineVoiceSynthesizer.Supported && (engine ?? SpeechPreferences.Engine) != SpeechEngine.System
            ? (await packs.Get(engine ?? SpeechPreferences.Engine).StateAsync(token)).Speaker : null;
    public Task ValidateAsync(string text, int speaker, CancellationToken token, SpeechEngine? engine = null) => Synth(engine ?? SpeechPreferences.Engine).ValidateAsync(text, speaker, token);
    public async Task SpeakAsync(string text, int speaker, CancellationToken token, SpeechEngine? engine = null)
    {
        var synthesizer = Synth(engine ?? SpeechPreferences.Engine);
        // Numeric expansion can be much longer than the input. Keep native allocations bounded on phones.
        foreach (var chunk in OfflineVoiceInput.Chunks(text))
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(chunk)) await PlayWaveAsync(await synthesizer.GenerateAsync(chunk, speaker, token), token);
        }
    }
    public async Task SpeakPinyinAsync(string phonemes, int speaker, CancellationToken token)
    {
        var bytes = await Synth(SpeechEngine.Melo).GeneratePinyinAsync(phonemes, speaker, token);
        await PlayWaveAsync(bytes, token);
    }
    private static async Task PlayWaveAsync(byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            SpeechDiagnostics.Write($"wave-play bytes={bytes.Length}");
            using var stream = new MemoryStream(bytes, false);
            var player = Plugin.Maui.Audio.AudioManager.Current.CreateAsyncPlayer(stream);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(4));
            try { await player.PlayAsync(timeout.Token); SpeechDiagnostics.Write("wave-completed"); }
            finally
            {
                try { try { player.Stop(); } finally { player.Dispose(); } }
                catch (Exception e) { throw new PlaybackCleanupException(e); }
            }
        });
    }
}
