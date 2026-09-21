using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Audio;

/// <summary>AISHELL-3 phonemes, not Latin-letter TTS or guessed readings of carrier characters.</summary>
public static class PinyinVoiceInput
{
    private static readonly string[] Initials = ["zh", "ch", "sh", "b", "p", "m", "f", "d", "t", "n", "l", "g", "k", "h", "j", "q", "x", "r", "z", "c", "s"];
    private static readonly HashSet<string> Finals = ["a", "o", "e", "ai", "ei", "ao", "ou", "an", "en", "ang", "eng", "er", "i", "ia", "ie", "iao", "iou", "ian", "in", "iang", "ing", "iong", "u", "ua", "uo", "uai", "uei", "uan", "uen", "uang", "ueng", "ong", "v", "ve", "van", "vn", "ii", "iii"];

    public static string Syllable(string spelling, int tone)
    {
        if (tone is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(tone));
        if (string.IsNullOrEmpty(spelling) || spelling.Any(c => c is not (>= 'a' and <= 'z') and not 'ü'))
            throw new InvalidDataException("Unsupported teaching syllable.");
        var value = spelling.Replace('ü', 'v');
        value = value switch
        {
            "yi" => "i", "ya" => "ia", "ye" => "ie", "yao" => "iao", "you" => "iou", "yan" => "ian", "yin" => "in", "yang" => "iang", "ying" => "ing", "yong" => "iong",
            "wu" => "u", "wa" => "ua", "wo" => "uo", "wai" => "uai", "wei" => "uei", "wan" => "uan", "wen" => "uen", "wang" => "uang", "weng" => "ueng",
            "yu" => "v", "yue" => "ve", "yuan" => "van", "yun" => "vn", _ => value
        };
        var initial = Initials.FirstOrDefault(i => value.StartsWith(i, StringComparison.Ordinal)) ?? "^";
        var final = initial == "^" ? value : value[initial.Length..];
        if (initial is "j" or "q" or "x" && final.StartsWith('u')) final = "v" + final[1..];
        final = final switch { "iu" => "iou", "ui" => "uei", "un" => "uen", _ => final };
        if (final == "i" && initial is "z" or "c" or "s") final = "ii";
        if (final == "i" && initial is "zh" or "ch" or "sh" or "r") final = "iii";
        if (!Finals.Contains(final)) throw new InvalidDataException("Unsupported teaching syllable.");
        return $"{initial} {final}{(tone == 0 ? 5 : tone)} #0";
    }

    public static string Demo(PinyinTeachingItem item)
    {
        if (item.Group is "initial" or "spelling")
        {
            var spelling = item.Display switch { "b" => "bo", "p" => "po", "m" => "mo", "f" => "fo", "d" => "de", "t" => "te", "n" => "ne", "l" => "le", "g" => "ge", "k" => "ke", "h" => "he", "j" => "ji", "q" => "qi", "x" => "xi", "zh" => "zhi", "ch" => "chi", "sh" => "shi", "r" => "ri", "z" => "zi", "c" => "ci", "s" => "si", "y" => "yi", "w" => "wu", _ => throw new InvalidDataException("Unknown teaching initial.") };
            return Syllable(spelling, 1);
        }
        return Syllable(item.Display, 1);
    }

    public static string Example(TextUnit unit)
    {
        if (unit.Tokens.Count is < 1 or > 32 || unit.Tokens.Any(t => t.Kind != TokenKind.Hanzi || t.Pinyin is null || t.Pinyin.Erhua))
            throw new InvalidDataException("Teaching speech requires explicit readings for every character.");
        return string.Join(' ', unit.Tokens.Select(t => Syllable(t.Pinyin!.Base, t.Pinyin.Tone)));
    }
}
