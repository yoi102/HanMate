using System.Globalization;

namespace HanMate.Core.Content;

public static class TextElementMap
{
    public static IReadOnlyList<int> CreateUtf16Boundaries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var starts = StringInfo.ParseCombiningCharacters(text);
        var result = new int[starts.Length + 1];
        starts.CopyTo(result, 0);
        result[^1] = text.Length;
        return result;
    }

    public static string Slice(string text, IReadOnlyList<int> boundaries, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(boundaries);
        if (start < 0 || length < 0 || start + length >= boundaries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        var utf16Start = boundaries[start];
        var utf16End = boundaries[start + length];
        return text[utf16Start..utf16End];
    }
}
