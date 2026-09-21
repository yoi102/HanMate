using HanMate.Core.Audio;
using HanMate.Core.Content;

namespace HanMate.App.Audio;

/// <summary>Call after the user's own/imported recording has been resolved.</summary>
public sealed class WordSpeechService(TextSpeechService speech)
{
    private readonly Lazy<Task<WordRecordingCatalog>> _catalog = new(LoadAsync);
    public Task<WordRecordingCatalog> GetCatalogAsync() => _catalog.Value;

    private static async Task<WordRecordingCatalog> LoadAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("WordAudio/catalog.json");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        return await Task.Run(() => WordRecordingCatalog.Parse(json));
    }

    public async Task SpeakAsync(string text, IReadOnlyList<PinyinSyllable?> readings, Func<string>? phonemes, CancellationToken token)
    {
        var catalog = await GetCatalogAsync().WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (catalog.Find(text, readings) is { } asset)
        {
            SpeechDiagnostics.Write($"headword-recording asset={asset.Key}");
            await BundledPronunciationService.PlayAssetAsync(asset, token);
            return;
        }
        await speech.SpeakAsync(text, phonemes, token);
    }
}
