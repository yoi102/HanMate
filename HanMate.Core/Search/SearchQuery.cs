using System.Text;

namespace HanMate.Core.Search;

public enum SearchMatchTier { HanziExact, PinyinExact, Prefix, Contains }

/// <summary>A query copy; the user's input and confirmed pronunciation are never rewritten.</summary>
public sealed class SearchQuery
{
    private readonly HashSet<int> _boundaries = [];
    private readonly List<(int Position, int Tone, bool End)> _tones = [];
    public string Key { get; private set; } = "";
    public bool IsPinyin { get; private set; }
    public bool IsValid { get; private set; }

    public static SearchQuery Parse(string? input)
    {
        var query = new SearchQuery();
        if (string.IsNullOrWhiteSpace(input) || input.Length > 256) return query;
        string text;
        try { text = input.Trim().Normalize().ToLowerInvariant().Replace("u:", "ü"); }
        catch (ArgumentException) { return query; }
        if (text.EnumerateRunes().Any(r => r.Value is >= 0x3400 and <= 0x9fff or >= 0x20000 and <= 0x323af))
        {
            query.Key = text; query.IsValid = !text.Any(char.IsControl); return query;
        }
        query.IsPinyin = true;
        var key = new StringBuilder();
        const string marked = "āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜńňǹḿ";
        const string bases =  "aaaaeeeeiiiioooouuuuvvvvnnnm";
        int[] tones = [1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,1,2,3,4,2,3,4,2];
        var afterDigit = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch is '\'' or '’' or '‘')
            { if (key.Length == 0) return query; query._boundaries.Add(key.Length); afterDigit = false; continue; }
            if (ch is >= '0' and <= '5')
            {
                if (key.Length == 0 || afterDigit || query._boundaries.Contains(key.Length)) return query;
                query._tones.Add((key.Length, ch is '0' or '5' ? 0 : ch - '0', true));
                query._boundaries.Add(key.Length); afterDigit = true; continue;
            }
            var markedIndex = marked.IndexOf(ch);
            if (markedIndex >= 0)
            { query._tones.Add((key.Length, tones[markedIndex], false)); key.Append(bases[markedIndex]); }
            else if (ch is >= 'a' and <= 'z' || ch == 'ü') key.Append(ch == 'ü' ? 'v' : ch);
            else return query;
            afterDigit = false;
        }
        query.Key = key.ToString(); query.IsValid = key.Length > 0;
        return query;
    }

    public SearchMatchTier? Match(string hanzi, string separated, string toneSeparated)
    {
        if (!IsValid) return null;
        if (!IsPinyin)
        {
            var index = hanzi.IndexOf(Key, StringComparison.Ordinal);
            return index < 0 ? null : index == 0 && hanzi.Length == Key.Length ? SearchMatchTier.HanziExact
                : index == 0 ? SearchMatchTier.Prefix : SearchMatchTier.Contains;
        }
        var syllables = separated.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tones = toneSeparated.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (syllables.Length == 0 || tones.Length != syllables.Length) return null;
        var joined = string.Concat(syllables);
        var ends = new int[syllables.Length];
        for (var i = 0; i < ends.Length; i++) ends[i] = (i == 0 ? 0 : ends[i - 1]) + syllables[i].Length;
        for (var offset = joined.IndexOf(Key, StringComparison.Ordinal); offset >= 0;
             offset = joined.IndexOf(Key, offset + 1, StringComparison.Ordinal))
        {
            if (_boundaries.Any(b => !ends.Contains(offset + b))) continue;
            var specified = new Dictionary<int, int>();
            var valid = true;
            foreach (var constraint in _tones)
            {
                var syllable = Array.FindIndex(ends, end => constraint.End ? end == offset + constraint.Position : end > offset + constraint.Position);
                if (syllable < 0 || tones[syllable].Length == 0 || tones[syllable][^1] - '0' != constraint.Tone
                    || specified.TryGetValue(syllable, out var previous) && previous != constraint.Tone)
                { valid = false; break; }
                specified[syllable] = constraint.Tone;
            }
            if (valid) return offset == 0 && joined.Length == Key.Length ? SearchMatchTier.PinyinExact
                : offset == 0 ? SearchMatchTier.Prefix : SearchMatchTier.Contains;
        }
        return null;
    }
}
