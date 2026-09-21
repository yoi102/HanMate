using System.Text;

namespace HanMate.Core.Content;

public static class PinyinFormatter
{
    private const string Vowels = "aoeiuvü";
    private static readonly string[] Marks = ["āáǎà", "ōóǒò", "ēéěè", "īíǐì", "ūúǔù", "ǖǘǚǜ", "ǖǘǚǜ"];

    public static string Format(string baseSyllable, int tone, bool erhua)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseSyllable);
        if (tone is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(tone));

        var normalized = baseSyllable.Trim().ToLowerInvariant().Replace("u:", "ü", StringComparison.Ordinal).Replace('v', 'ü');
        if (normalized.Any(char.IsWhiteSpace)) throw new ArgumentException("A syllable cannot contain whitespace.", nameof(baseSyllable));

        if (tone > 0)
        {
            var index = FindToneIndex(normalized);
            if (index < 0) throw new ArgumentException("The syllable does not contain a supported vowel.", nameof(baseSyllable));
            var vowelIndex = Vowels.IndexOf(normalized[index]);
            normalized = normalized[..index] + Marks[vowelIndex][tone - 1] + normalized[(index + 1)..];
        }

        if (erhua && !normalized.EndsWith('r')) normalized += "r";
        return normalized.Normalize(NormalizationForm.FormC);
    }

    private static int FindToneIndex(string syllable)
    {
        var index = syllable.IndexOf('a');
        if (index >= 0) return index;
        index = syllable.IndexOf('e');
        if (index >= 0) return index;
        index = syllable.IndexOf("ou", StringComparison.Ordinal);
        if (index >= 0) return index;
        for (var i = syllable.Length - 1; i >= 0; i--)
            if (Vowels.Contains(syllable[i])) return i;
        return -1;
    }
}
