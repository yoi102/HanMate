using HanMate.Core.Pinyin;

// Platform boundaries only: these doubles prove routing, not native playback or pronunciation.
namespace HanMate.App.Audio;

internal static class Preferences
{
    public static TestPreferences Default { get; } = new();
    internal sealed class TestPreferences
    {
        private readonly Dictionary<string, string> _values = [];
        public string Get(string key, string fallback) => _values.GetValueOrDefault(key, fallback);
        public void Set(string key, string value) => _values[key] = value;
    }
}

internal static class FileSystem
{
    public static Task<Stream> OpenAppPackageFileAsync(string path) =>
        Task.FromResult<Stream>(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", path)));
}

internal static class SpeechDiagnostics { public static void Write(string message) { } }

public sealed record LocalVoice(string Id, string Name, string Language);
internal static class NativeSpeech
{
    public static List<string> Spoken { get; } = [];
    public static Task<LocalVoice[]> ListAsync(CancellationToken token) => Task.FromResult(new[] { new LocalVoice("test", "test", "zh-CN") });
    public static Task SpeakAsync(LocalVoice voice, string text, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Spoken.Add(text); return Task.CompletedTask; }
}

public sealed class AiVoiceService
{
    public bool Available { get; set; } = true;
    public List<(SpeechEngine Engine, string Input)> Calls { get; } = [];
    public Task<int?> SelectedAsync(CancellationToken token, SpeechEngine? engine = null) => Task.FromResult<int?>(Available ? 0 : null);
    public Task ValidateAsync(string text, int speaker, CancellationToken token, SpeechEngine? engine = null) => Task.CompletedTask;
    public Task SpeakAsync(string text, int speaker, CancellationToken token, SpeechEngine? engine = null)
    { token.ThrowIfCancellationRequested(); Calls.Add((engine ?? SpeechPreferences.Engine, text)); return Task.CompletedTask; }
    public Task SpeakPinyinAsync(string phonemes, int speaker, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Calls.Add((SpeechEngine.Melo, phonemes)); return Task.CompletedTask; }
}

internal static class BundledPronunciationService
{
    public static List<PronunciationAsset> Played { get; } = [];
    public static Task PlayAssetAsync(PronunciationAsset asset, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Played.Add(asset); return Task.CompletedTask; }
}
