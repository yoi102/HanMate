using System.Globalization;
using System.Text;

namespace HanMate.Core.Audio;

public sealed class UnsupportedVoiceTextException() : Exception("Offline voice cannot pronounce this text.");

public static class OfflineVoiceInput
{
    // Normalize before splitting so invisible markup cannot split words and long
    // separators cannot create a punctuation-only request. Validation and playback
    // use this same plan; never drop an unknown letter, Hanzi or number.
    public static IReadOnlyList<string> Chunks(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized)) throw new UnsupportedVoiceTextException();
        var chunks = SpeechText.Split(normalized, 48).Where(c => c.EnumerateRunes().Any(r => !IsPause(r))).ToArray();
        if (chunks.Length == 0) throw new UnsupportedVoiceTextException();
        return chunks;
    }

    public static void Validate(string text, Func<string, bool> knownCharacter, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        foreach (var chunk in Chunks(text))
        {
            token.ThrowIfCancellationRequested();
            Prepare(chunk, knownCharacter);
        }
    }

    /// <summary>Reject unsupported words before native inference can omit or log them.</summary>
    public static string Prepare(string text, Func<string, bool> knownCharacter)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw new UnsupportedVoiceTextException();
        var result = new StringBuilder(); var hasSpeech = false;
        foreach (var rune in Normalize(text).EnumerateRunes())
        {
            var value = rune.ToString();
            if (IsPause(rune)) result.Append(value);
            else if (knownCharacter(value)) { result.Append(value); hasSpeech = true; }
            else if (rune.Value is >= '0' and <= '9') { result.Append(value); hasSpeech = true; }
            else throw new UnsupportedVoiceTextException();
        }
        if (!hasSpeech) throw new UnsupportedVoiceTextException();
        return result.ToString();
    }

    /// <summary>Speech-only cleanup. The saved text, visible text and annotations remain intact.</summary>
    public static string Normalize(string text)
    {
        if (text.Length is 0 or > 100000 || text.Contains('\0')) throw new UnsupportedVoiceTextException();
        var result = new StringBuilder(text.Length);
        foreach (var original in text.EnumerateRunes())
        {
            // Copy/paste layout artifacts, not all Unicode marks (tone/combining
            // marks and unknown writing systems must not silently disappear).
            if (original.Value is 0xfeff or 0x200b or 0x200c or 0x200d or 0x2060 or 0x00ad or 0x200e or 0x200f or
                >= 0x202a and <= 0x202e or >= 0x2066 and <= 0x2069 or
                >= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef) continue;
            var rune = original.Value is >= 0xff01 and <= 0xff5e && original.Value is not (0xff0c or 0xff01 or 0xff1f or 0xff1b or 0xff1a)
                ? new Rune(original.Value - 0xfee0) : original;
            // Circled list numbers retain their value rather than being omitted.
            if (rune.Value is >= 0x2460 and <= 0x2473)
            { result.Append((rune.Value - 0x2460 + 1).ToString(CultureInfo.InvariantCulture)); continue; }
            if (rune.Value == 0x24ea) { result.Append('0'); continue; }
            var value = rune.ToString();
            // The reader treats special symbols as separators, as requested. Keep
            // letters/numbers (including unfamiliar scripts) for the normal lexicon
            // check, rather than hiding an unsupported word in an otherwise valid paragraph.
            if (Rune.IsWhiteSpace(rune) || Rune.IsSymbol(rune) || rune.Value == 0x20e3) value = "，";
            // Both models support comma/full-stop/question/exclamation pauses;
            // colon/semicolon are otherwise silently ignored by Melo's frontend.
            else if (Rune.IsPunctuation(rune) && !",.!?-。！？".Contains(value, StringComparison.Ordinal)) value = "，";
            // Keep decimal dots and minus signs, but bound repeated decorative
            // punctuation/whitespace to one pause. Do not glue paragraphs together.
            var pause = value.Length == 1 && ",!?。！？，".Contains(value, StringComparison.Ordinal);
            if (pause && result.Length > 0 && ",!?。！？，".Contains(result[^1]))
            {
                if ("。！？.!?".Contains(value, StringComparison.Ordinal)) result[^1] = value[0];
                continue;
            }
            result.Append(value);
        }
        return result.ToString();
    }

    private static bool IsPause(Rune rune) => rune.IsAscii && ",.!?-".Contains((char)rune.Value) ||
        "。！？，".Contains(rune.ToString(), StringComparison.Ordinal);
}
