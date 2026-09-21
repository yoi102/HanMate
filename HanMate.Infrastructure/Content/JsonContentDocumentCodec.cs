using System.Text.Json;
using HanMate.Core.Content;

namespace HanMate.Infrastructure.Content;

public sealed class JsonContentDocumentCodec(ContentDocumentValidator validator)
{
    public ContentDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = JsonSerializer.Deserialize<ContentDocument>(stream, ContentJson.Options)
            ?? throw new JsonException("Content document is null.");
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new ContentDocumentValidationException(validation.Errors);
        return document;
    }

    public void Write(Stream stream, ContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var validation = validator.Validate(document);
        if (!validation.IsValid) throw new ContentDocumentValidationException(validation.Errors);
        JsonSerializer.Serialize(stream, document, ContentJson.Options);
    }
}

public sealed class ContentDocumentValidationException(IReadOnlyList<ContentValidationError> errors)
    : Exception($"Content document failed validation: {string.Join(", ", errors.Select(error => error.Code))}")
{
    public IReadOnlyList<ContentValidationError> Errors { get; } = errors;
}
