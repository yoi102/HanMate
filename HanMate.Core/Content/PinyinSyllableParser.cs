using System.Globalization;
using System.Text;

namespace HanMate.Core.Content;

public static class PinyinSyllableParser
{
    private static readonly IReadOnlyDictionary<char, int> ToneMarks = new Dictionary<char, int>
    {
        ['\u0304'] = 1,
        ['\u0301'] = 2,
        ['\u030c'] = 3,
        ['\u0300'] = 4
    };

    public static PinyinSyllable ParseDictionarySyllable(string value)
    {
        if (!TryParseDictionarySyllable(value, out var syllable))
        {
            throw new FormatException($"Unsupported pinyin syllable: '{value}'.");
        }

        return syllable;
    }

    public static bool TryParseDictionarySyllable(string? value, out PinyinSyllable syllable)
    {
        syllable = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var input = value.Trim().ToLowerInvariant().Replace("u:", "ü", StringComparison.Ordinal).Replace('v', 'ü');
        var digitTone = ParseTrailingToneDigit(ref input);
        if (digitTone == -1) return false;

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var baseBuilder = new StringBuilder(decomposed.Length);
        int? markedTone = null;

        foreach (var character in decomposed)
        {
            if (ToneMarks.TryGetValue(character, out var tone))
            {
                if (markedTone is not null) return false;
                markedTone = tone;
                continue;
            }

            if (character == '\u0308')
            {
                if (baseBuilder.Length == 0 || baseBuilder[^1] != 'u') return false;
                baseBuilder[^1] = 'ü';
                continue;
            }

            if (character == '\u0302')
            {
                if (baseBuilder.Length == 0 || baseBuilder[^1] != 'e') return false;
                baseBuilder[^1] = 'ê';
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                return false;
            }

            if (!(character is >= 'a' and <= 'z') && character is not 'ü' and not 'ê') return false;
            baseBuilder.Append(character);
        }

        if (baseBuilder.Length == 0 || (digitTone >= 0 && markedTone is not null)) return false;

        var baseSyllable = baseBuilder.ToString();
        var erhua = baseSyllable.Length > 1 && baseSyllable.EndsWith('r') && !baseSyllable.Equals("er", StringComparison.Ordinal);
        if (erhua) baseSyllable = baseSyllable[..^1];

        var resolvedTone = markedTone ?? (digitTone >= 0 ? digitTone : 0);
        try
        {
            var display = PinyinFormatter.Format(baseSyllable, resolvedTone, erhua);
            syllable = new PinyinSyllable
            {
                Base = baseSyllable,
                Tone = resolvedTone,
                Erhua = erhua,
                Display = display
            };
            return markedTone is null || string.Equals(display, input.Normalize(NormalizationForm.FormC), StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int ParseTrailingToneDigit(ref string input)
    {
        var digits = input.Where(char.IsAsciiDigit).ToArray();
        if (digits.Length == 0) return -2;
        if (digits.Length != 1 || !char.IsAsciiDigit(input[^1])) return -1;

        var digit = input[^1] - '0';
        if (digit is < 0 or > 5) return -1;
        input = input[..^1];
        return digit == 5 ? 0 : digit;
    }

}
