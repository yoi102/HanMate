using System.Text;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Audio;

public sealed record WordRecordingData(string Version, IReadOnlyList<PronunciationAsset> Assets);

/// <summary>Exact headword/reading bindings. Syllable recordings are only eligible for one Hanzi.</summary>
public sealed class WordRecordingCatalog
{
    public WordRecordingData Data { get; }
    private readonly Dictionary<(string Text, string Reading), PronunciationAsset> _bindings = [];
    private readonly Dictionary<string, PronunciationAsset> _assets = new(StringComparer.Ordinal);

    public WordRecordingCatalog(WordRecordingData data)
    {
        Data = data;
        foreach (var asset in data.Assets)
        {
            var syllables = (asset.Pinyin ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var readings = syllables.Select(p => PinyinSyllableParser.TryParseDictionarySyllable(p, out var value) ? value : null).ToArray();
            var signature = Signature(readings);
            if (signature is null || (asset.Text is null ? readings.Length != 1 : !IsHanzi(asset.Text, readings.Length)) ||
                asset.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != 'ü') || asset.Key.Length == 0 ||
                (asset.File != "WordAudio/" + asset.Key + ".mp3" && asset.File != "Pinyin/" + asset.Key + ".wav") ||
                asset.Sha256.Length != 64 || asset.Sha256.Any(c => !char.IsAsciiHexDigit(c)) ||
                !double.IsFinite(asset.DurationSeconds) || asset.DurationSeconds is <= 0 or > 15 ||
                string.IsNullOrWhiteSpace(asset.Author) || string.IsNullOrWhiteSpace(asset.License))
                throw new InvalidDataException("Invalid headword recording binding.");
            if (!_bindings.TryAdd((asset.Text ?? "", signature), asset))
                throw new InvalidDataException("Duplicate headword recording binding.");
            if (_assets.TryGetValue(asset.Key, out var prior) && (prior.File != asset.File || prior.Sha256 != asset.Sha256))
                throw new InvalidDataException("Conflicting headword recording asset.");
            _assets[asset.Key] = asset;
        }
    }

    public PronunciationAsset? Find(TextUnit unit) => unit.Role == TextUnitRole.Headword &&
        string.Concat(unit.Tokens.Select(t => t.Text)) == unit.Text && unit.Tokens.All(t => t.Kind == TokenKind.Hanzi)
        ? Find(unit.Text, unit.Tokens.Select(t => t.Pinyin).ToArray()) : null;

    public PronunciationAsset? Find(string text, IReadOnlyList<PinyinSyllable?> readings)
    {
        var signature = Signature(readings);
        if (signature is null || !IsHanzi(text, readings.Count)) return null;
        return _bindings.GetValueOrDefault((text, signature)) ??
            (readings.Count == 1 ? _bindings.GetValueOrDefault(("", signature)) : null);
    }

    public PronunciationAsset GetAsset(string key) => _assets.TryGetValue(key, out var asset)
        ? asset : throw new InvalidDataException("Unknown headword recording.");

    private static string? Signature(IReadOnlyList<PinyinSyllable?> readings)
    {
        if (readings.Count is < 1 or > 32 || readings.Any(p => p is null || p.Erhua || p.Tone is < 0 or > 4 ||
            !PinyinSyllableParser.TryParseDictionarySyllable(p.Base + p.Tone, out var parsed) || parsed.Erhua)) return null;
        return string.Join(' ', readings.Select(p => p!.Base.Replace("u:", "ü", StringComparison.Ordinal).Replace('v', 'ü').ToLowerInvariant() + p.Tone));
    }

    private static bool IsHanzi(string text, int count)
    {
        var runes = text.EnumerateRunes().ToArray();
        return runes.Length == count && runes.All(r => r.Value is >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff or >= 0x20000 and <= 0x323af);
    }

    public static WordRecordingCatalog Parse(string json) => new(JsonSerializer.Deserialize<WordRecordingData>(json, ContentJson.Options)
        ?? throw new InvalidDataException("Missing headword recording catalog."));
}
