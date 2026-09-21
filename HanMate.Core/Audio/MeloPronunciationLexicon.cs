using System.Text;

namespace HanMate.Core.Audio;

/// <summary>A fixed private-use alphabet for exact Melo phone/tone input; never spoken as carrier words.</summary>
public sealed class MeloPronunciationLexicon
{
    private readonly Dictionary<(string Phone, int Tone), char> _aliases = [];
    public string Entries { get; }

    public MeloPronunciationLexicon(IEnumerable<string> phones)
    {
        var entries = new StringBuilder();
        var next = 0xe000;
        foreach (var phone in phones.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(phone) || phone.Any(char.IsWhiteSpace))
                throw new InvalidDataException("Invalid Melo token.");
            for (var tone = 1; tone <= 5; tone++)
            {
                if (next > 0xf8ff) throw new InvalidDataException("Too many Melo tokens.");
                var alias = (char)next++;
                _aliases.Add((phone, tone), alias);
                entries.Append(alias).Append(' ').Append(phone).Append(' ').Append(tone).Append('\n');
            }
        }
        Entries = entries.ToString();
    }

    public string Encode(string teachingPhonemes)
    {
        var values = MeloVoiceInput.FromTeachingPhonemes(teachingPhonemes).Split(' ');
        var count = values.Length / 2;
        var encoded = new StringBuilder(count);
        for (var i = 0; i < count; i++)
        {
            if (!int.TryParse(values[i + count], out var tone) || !_aliases.TryGetValue((values[i], tone), out var alias))
                throw new InvalidDataException("Unsupported teaching phonemes.");
            encoded.Append(alias);
        }
        return encoded.ToString();
    }

    // Melo's reference g2p includes a neutral boundary phone at each end, before
    // the model's add_blank step. A bare one-character lexicon entry omits these.
    public static string WithBoundaries(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Empty Melo input.");
        const string sentencePauses = ".,!?。，！？";
        // A terminal punctuation already supplies a pause. Appending '_' after it
        // creates a separate blank-only sentence in sherpa's sentence splitter.
        return (sentencePauses.Contains(text[0]) ? "" : "_") + text +
            (sentencePauses.Contains(text[^1]) ? "" : "_");
    }
}
