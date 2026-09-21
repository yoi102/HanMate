using System.Globalization;
using System.Text;

namespace HanMate.Core.Audio;

public sealed class UnsupportedVoiceTextException() : Exception("Offline voice cannot pronounce this text.");

public static class OfflineVoiceInput
{
    // Preflight, reader and preview must use identical native boundaries. Numeric expansion is bounded at 48.
    public static IReadOnlyList<string> Chunks(string text) => SpeechText.Split(text, 48).Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();

    public static void Validate(string text, Func<string, bool> knownCharacter, CancellationToken token = default)
    {
        foreach (var chunk in Chunks(text))
        {
            token.ThrowIfCancellationRequested();
            Prepare(chunk, knownCharacter);
        }
    }

    /// <summary>Reject unsupported words before native inference can omit or log them. Normalize only pauses.</summary>
    public static string Prepare(string text, Func<string, bool> knownCharacter)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw new UnsupportedVoiceTextException();
        var result = new StringBuilder(); var hasSpeech = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.ToString();
            if (knownCharacter(value)) { result.Append(value); hasSpeech = true; }
            else if (rune.Value is >= '0' and <= '9') { result.Append(value); hasSpeech = true; }
            else if (rune.Value is >= 0xff10 and <= 0xff19) { result.Append((char)('0' + rune.Value - 0xff10)); hasSpeech = true; }
            else if (",.;!?-:。；！？：”，、“".Contains(value, StringComparison.Ordinal)) result.Append(value);
            else if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.ConnectorPunctuation)
                result.Append('，');
            else throw new UnsupportedVoiceTextException();
        }
        if (!hasSpeech) throw new UnsupportedVoiceTextException();
        return result.ToString();
    }
}
