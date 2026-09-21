using System.Globalization;
using System.Text;

namespace HanMate.Core.Content;

public sealed record TextDraft(string Title, string Text, ContentKind Kind = ContentKind.Text, string Format = "textDraft.v1")
{
    public string Pattern { get; init; } = "";
    public string Example { get; init; } = "";
    public ContentDocument? Annotation { get; init; }
    public ContentDocument? PreviousAnnotation { get; init; }
    public string? EngineVersion { get; init; }
    public long ExpectedContentRevision { get; init; }
    public bool StructuredOnly { get; init; }
}

public static class TextDraftInput
{
    public const int MaxBytes = 1024 * 1024;
    public static void Validate(TextDraft draft)
    {
        if (draft.Format is not ("textDraft.v1" or "textDraft.v2") || !Enum.IsDefined(draft.Kind) || new StringInfo(draft.Title).LengthInTextElements > 120 ||
            new StringInfo(draft.Text).LengthInTextElements + new StringInfo(draft.Example).LengthInTextElements > 20000 ||
            draft.Pattern.Length > 6480 || Encoding.UTF8.GetByteCount(draft.Text + draft.Example) > MaxBytes || draft.ExpectedContentRevision < 0)
            throw new InvalidDataException("TEXT_LIMIT_OR_FORMAT");
        // Throw on unpaired UTF-16 surrogates from paste/input instead of saving replacement characters.
        _ = new UTF8Encoding(false, true).GetByteCount(draft.Text);
        _ = new UTF8Encoding(false, true).GetByteCount(draft.Title + draft.Example + draft.Pattern);
    }
    public static async Task<string> ReadAsync(Stream input, CancellationToken token = default)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
        while ((read = await input.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new InvalidDataException("TEXT_LIMIT_OR_FORMAT");
            buffer.Write(chunk, 0, read);
        }
        token.ThrowIfCancellationRequested(); var bytes = buffer.ToArray(); var skip = 0;
        Encoding encoding = new UTF8Encoding(false, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) skip = 3;
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        var text = encoding.GetString(bytes, skip, bytes.Length - skip);
        if (text.Contains('\0')) throw new InvalidDataException("TEXT_ENCODING");
        Validate(new("", text)); return text;
    }
}
