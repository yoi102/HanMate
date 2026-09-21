using System.Globalization;

namespace HanMate.Core.Audio;

public static class SpeechText
{
    /// <summary>Bound native utterances without splitting Unicode text elements or changing the text.</summary>
    public static IReadOnlyList<string> Split(string text, int elementLimit = 180)
    {
        if (elementLimit is < 1 or > 180) throw new ArgumentOutOfRangeException(nameof(elementLimit));
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100000) throw new InvalidDataException("Invalid speech text.");
        var result = new List<string>();
        var offsets = StringInfo.ParseCombiningCharacters(text);
        var start = 0;
        while (start < offsets.Length)
        {
            var end = start; var boundary = -1;
            while (end < offsets.Length && end - start < elementLimit)
            {
                var next = end + 1 < offsets.Length ? offsets[end + 1] : text.Length;
                if (next - offsets[end] > 500) throw new InvalidDataException("Text element too long for speech.");
                if (next - offsets[start] > 500) break;
                var element = text.AsSpan(offsets[end], next - offsets[end]);
                end++;
                if (char.IsWhiteSpace(element[0]) || "。！？；，、.!?;,：:".Contains(element[0])) boundary = end;
            }
            // Prefer a phrase boundary when the utterance exceeds the native limit.
            // Leave short utterances intact and preserve every character exactly once.
            if (end < offsets.Length && boundary > start) end = boundary;
            var stop = end < offsets.Length ? offsets[end] : text.Length;
            result.Add(text[offsets[start]..stop]); start = end;
        }
        return result;
    }
}
