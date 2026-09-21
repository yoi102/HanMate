using System.Globalization;
using System.Text;
using System.Text.Json;
using HanMate.Core.Content;

namespace HanMate.Core.Pinyin;

public sealed record PinyinExample(int Tone, Guid ContentId, Guid UnitId, Guid TokenId, string AudioKey, int PinyinStart, int PinyinLength, string? WordAudioKey = null);
public sealed record PinyinTeachingItem(Guid Id, string Group, string Display, IReadOnlyList<PinyinExample> Examples, bool SpellingNote, string? DemoAudioKey = null);
public sealed record PronunciationAsset(string Key, string File, string Sha256, string OriginalSha256, double DurationSeconds,
    string SourceUrl, string DownloadUrl, string Author, string License, string LicenseUrl, string Description, string Changes, string ReviewStatus,
    string? Text = null, string? Pinyin = null);
public sealed record PinyinCourseData(string Version, IReadOnlyList<PinyinTeachingItem> Items, IReadOnlyList<ContentDocument> Contents, IReadOnlyList<PronunciationAsset> Assets);

/// <summary>Read-only teaching selection; pronunciation files are examples, never bare Latin-letter TTS.</summary>
public sealed class PinyinCourse
{
    public PinyinCourseData Data { get; }
    private readonly IReadOnlyDictionary<Guid, ContentDocument> _contents;
    private readonly IReadOnlyDictionary<string, PronunciationAsset> _assets;
    private readonly IReadOnlyDictionary<Guid, string> _localPlayback;

    public PinyinCourse(PinyinCourseData data, IReadOnlyDictionary<Guid, string>? localPlayback = null)
    {
        Data = data;
        _localPlayback = localPlayback ?? new Dictionary<Guid, string>();
        _contents = data.Contents.ToDictionary(c => c.Id);
        _assets = data.Assets.ToDictionary(a => a.Key, StringComparer.Ordinal);
        var validator = new ContentDocumentValidator();
        if (data.Items.Select(i => i.Id).Distinct().Count() != data.Items.Count || data.Contents.Any(c => !validator.Validate(c).IsValid))
            throw new InvalidDataException("Invalid teaching content.");
        foreach (var item in data.Items)
        {
            if (item.Examples.Select(e => (e.UnitId, e.TokenId)).Distinct().Count() != item.Examples.Count) throw new InvalidDataException("Duplicate example.");
            if (item.DemoAudioKey is { } demo && !_assets.ContainsKey(demo)) throw new InvalidDataException("Missing teaching pronunciation.");
            foreach (var example in item.Examples)
            {
                var unit = Unit(example);
                var token = unit.Tokens.Single(t => t.Id == example.TokenId);
                if (example.Tone is < 1 or > 4 || token.Pinyin?.Tone != example.Tone || example.AudioKey != token.Pinyin.Base + example.Tone)
                    throw new InvalidDataException("Mismatched pronunciation.");
                var elements = StringInfo.ParseCombiningCharacters(token.Pinyin.Display);
                if (example.PinyinStart < 0 || example.PinyinLength <= 0 || example.PinyinStart + example.PinyinLength > elements.Length)
                    throw new InvalidDataException("Invalid pinyin highlight.");
                var plain = new string(token.Pinyin.Display.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
                var highlighted = plain.Substring(example.PinyinStart, example.PinyinLength);
                var expected = item.Display.Replace('ü', 'u');
                if (highlighted != expected) throw new InvalidDataException("Highlight does not match teaching item.");
                if (example.WordAudioKey is { } wordKey && (wordKey != "word-" + unit.Id.ToString("N") || !_assets.ContainsKey(wordKey)))
                    throw new InvalidDataException("Mismatched complete-word recording.");
            }
        }
        foreach (var asset in data.Assets)
        {
            if (asset.File != "Pinyin/" + asset.Key + ".wav" || asset.Sha256.Length != 64 || asset.Sha256.Any(c => !char.IsAsciiHexDigit(c)) ||
                !Uri.TryCreate(asset.LicenseUrl, UriKind.Absolute, out var license) ||
                !(license.Host == "creativecommons.org" || asset.LicenseUrl == "https://github.com/hugolpz/audio-cmn/blob/ff9ed3d0c631195bd2c06f39450f3264c7124040/README.md") ||
                asset.DurationSeconds is <= 0 or > 15 || string.IsNullOrWhiteSpace(asset.Author))
                throw new InvalidDataException("Invalid bundled audio provenance.");
        }
    }

    public TextUnit Unit(PinyinExample example) => _contents[example.ContentId].TextUnits.Single(u => u.Id == example.UnitId);
    public ContentDocument Content(PinyinExample example) => _contents[example.ContentId];
    public IReadOnlyList<PinyinExample> ExamplesForTone(PinyinTeachingItem item, int tone)
    {
        if (!Data.Items.Contains(item)) throw new ArgumentException("Unknown teaching item.");
        var examples = item.Examples.Where(e => e.Tone == tone).ToArray();
        var ordered = new List<PinyinExample>();
        var used = new HashSet<PinyinExample>();
        foreach (var character in examples.Where(e => Unit(e).Tokens.Count == 1))
        {
            ordered.Add(character); used.Add(character);
            var token = Unit(character).Tokens[0];
            foreach (var word in examples.Where(e => Unit(e).Tokens.Count > 1))
            {
                var target = Unit(word).Tokens.Single(t => t.Id == word.TokenId);
                if (target.Text == token.Text && target.Pinyin?.Base == token.Pinyin?.Base &&
                    target.Pinyin?.Tone == token.Pinyin?.Tone && used.Add(word)) ordered.Add(word);
            }
        }
        foreach (var example in examples)
            if (used.Add(example)) ordered.Add(example);
        return ordered;
    }
    public PronunciationAsset? Audio(PinyinExample example)
    {
        var unit = Unit(example);
        if (example.WordAudioKey is { } key)
        {
            var asset = _assets.GetValueOrDefault(key);
            return asset?.Text == unit.Text && asset.Pinyin == string.Join(" ", unit.Tokens.Select(t => t.Pinyin?.Base + t.Pinyin?.Tone)) ? asset : null;
        }
        // These assets describe one syllable. They must not masquerade as a recording of a longer example word.
        if (unit.Tokens.Count != 1 || unit.Tokens[0].Id != example.TokenId || unit.Text != unit.Tokens[0].Text) return null;
        return _assets.GetValueOrDefault(example.AudioKey);
    }
    public PinyinExample? ClickExample(PinyinTeachingItem item)
    {
        if (!Data.Items.Contains(item)) throw new ArgumentException("Unknown teaching item.");
        return item.Examples.OrderBy(e => e.Tone).FirstOrDefault(e => PlaybackKey(e) is not null);
    }
    public string? DemoPlaybackKey(PinyinTeachingItem item)
    {
        if (!Data.Items.Contains(item)) throw new ArgumentException("Unknown teaching item.");
        if (item.DemoAudioKey is not null) return item.DemoAudioKey;
        // An absent isolated bundled final must not be replaced with an unrelated syllable.
        if (item.Group == "nasal" && item.Display == "ong") return null;
        return ClickExample(item) is { } example ? PlaybackKey(example) : null;
    }
    public string? PlaybackKey(PinyinExample example) => _localPlayback.GetValueOrDefault(example.UnitId) ?? (Audio(example) is null ? null : example.WordAudioKey ?? example.AudioKey);
    public static PinyinCourse Parse(string json) => new(JsonSerializer.Deserialize<PinyinCourseData>(json, ContentJson.Options) ?? throw new InvalidDataException("Missing course."));
}
