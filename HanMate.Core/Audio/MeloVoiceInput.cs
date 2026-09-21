namespace HanMate.Core.Audio;

/// <summary>Convert explicit Mandarin syllables to Melo's phones followed by per-phone tones.</summary>
public static class MeloVoiceInput
{
    public static string FromTeachingPhonemes(string input)
    {
        var source = input.Split(' '); var phones = new List<string>(); var tones = new List<string>();
        if (source.Length is < 3 or > 96 || source.Length % 3 != 0) throw new InvalidDataException("Invalid teaching phonemes.");
        for (var i = 0; i < source.Length; i += 3)
        {
            var initial = source[i]; var final = source[i + 1];
            if (final.Length < 2 || final[^1] is < '1' or > '5' || source[i + 2] != "#0")
                throw new InvalidDataException("Invalid teaching tone.");
            var tone = final[^1].ToString(); final = final[..^1];
            if (initial == "^")
            {
                (initial, final) = final switch
                {
                    "i" => ("y", "i"), "ia" => ("y", "a"), "ie" => ("y", "E"), "ian" => ("y", "En"),
                    "iang" => ("y", "ang"), "iao" => ("y", "ao"), "iong" => ("y", "ong"), "iou" => ("y", "ou"),
                    "in" => ("y", "in"), "ing" => ("y", "ing"),
                    "u" => ("w", "u"), "ua" => ("w", "a"), "uo" => ("w", "o"), "uai" => ("w", "ai"),
                    "uei" => ("w", "ei"), "uan" => ("w", "an"), "uen" => ("w", "en"), "uang" => ("w", "ang"), "ueng" => ("w", "eng"),
                    "v" or "ve" or "van" or "vn" => ("y", final),
                    "a" or "ai" or "an" or "ang" or "ao" => ("AA", final),
                    "e" or "ei" or "en" or "eng" or "er" => ("EE", final),
                    "o" or "ou" => ("OO", final),
                    "ong" => ("", "ong"), // Isolated final, no unrelated carrier consonant.
                    _ => throw new InvalidDataException("Unsupported teaching final.")
                };
            }
            else final = final switch { "iou" => "iu", "uei" => "ui", "uen" => "un", "ii" => "i0", "iii" => "ir", _ => final };
            if (initial.Length > 0) { phones.Add(initial); tones.Add(tone); }
            phones.Add(final); tones.Add(tone);
        }
        return string.Join(' ', phones.Concat(tones));
    }
}
