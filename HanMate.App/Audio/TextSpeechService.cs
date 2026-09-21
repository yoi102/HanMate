using HanMate.Core.Audio;

namespace HanMate.App.Audio;

public sealed class SpeechUnavailableException(bool system) : Exception("Selected speech provider is unavailable.")
{ public bool SystemVoice { get; } = system; }

public sealed class TextSpeechService(AiVoiceService ai)
{
    public static async Task<LocalVoice> SystemVoiceAsync(CancellationToken token)
    {
        var choices = await NativeSpeech.ListAsync(token);
        var id = SpeechPreferences.SystemVoiceId;
        return (id.Length == 0 ? choices.OrderByDescending(v => v.Language.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
            : choices.FirstOrDefault(v => v.Id == id) ?? choices.FirstOrDefault(v => v.Name == id)) ?? throw new SpeechUnavailableException(true);
    }
    public async Task SpeakAsync(string text, Func<string>? phonemes, CancellationToken token)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        SpeechDiagnostics.Write($"request provider={SpeechPreferences.Engine} chars={text.Length}");
        try { await SpeakCoreAsync(text, phonemes, token); }
        catch (Exception e) { SpeechDiagnostics.Write($"request-failed type={e.GetType().Name} inner={e.InnerException?.GetType().Name}"); throw; }
        finally { SpeechDiagnostics.Write($"request-ended ms={timer.ElapsedMilliseconds} cancelled={token.IsCancellationRequested}"); }
    }
    private async Task SpeakCoreAsync(string text, Func<string>? phonemes, CancellationToken token)
    {
        var engine = SpeechPreferences.Engine;
        if (engine == SpeechEngine.System)
        {
            var voice = await SystemVoiceAsync(token);
            foreach (var chunk in SpeechText.Split(text).Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                token.ThrowIfCancellationRequested();
                if (SpeechPreferences.Engine != engine) throw new OperationCanceledException();
                await NativeSpeech.SpeakAsync(voice, chunk, token);
            }
            return;
        }
        var speaker = await ai.SelectedAsync(token, engine) ?? throw new SpeechUnavailableException(false);
        if (engine == SpeechEngine.Melo && phonemes is not null)
            await ai.SpeakPinyinAsync(phonemes(), speaker, token);
        else
        {
            await ai.ValidateAsync(text, speaker, token, engine);
            await ai.SpeakAsync(text, speaker, token, engine);
        }
    }
}
