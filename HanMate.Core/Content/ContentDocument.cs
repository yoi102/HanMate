using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace HanMate.Core.Content;

public sealed record ContentCollection
{
    public int SchemaVersion { get; init; } = 2;
    public IReadOnlyList<ContentDocument> Contents { get; init; } = [];
}

public sealed record ContentDocument
{
    public int SchemaVersion { get; init; } = 2;
    public required Guid Id { get; init; }
    public required ContentKind Kind { get; init; }
    public required ContentOrigin Origin { get; init; }
    public required string Title { get; init; }
    public required long ContentRevision { get; init; }
    public required long AnnotationRevision { get; init; }
    public required long MetadataRevision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Difficulty? Difficulty { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public SchoolStage? SchoolStage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? Grade { get; init; }
    public IReadOnlyList<string> Scenes { get; init; } = [];
    public required ContentSource Source { get; init; }
    public IReadOnlyList<TextUnit> TextUnits { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public GrammarDefinition? Grammar { get; init; }
}

public sealed record ContentSource
{
    public required string SourceId { get; init; }
    public required SourceType Type { get; init; }
    public required string AuthorProvider { get; init; }
    public required string Reference { get; init; }
    public required string LicenseIdentifier { get; init; }
    public required string PermissionNotes { get; init; }
    public required bool CanDistribute { get; init; }
    public required bool CanShare { get; init; }
    public required BackupPolicy BackupPolicy { get; init; }
    public required ReviewStatus ReviewStatus { get; init; }
    public Guid? ResourceId { get; init; }
    public string? ResourceVersion { get; init; }
    public string? EntryId { get; init; }
}

public sealed record TextUnit
{
    public required Guid Id { get; init; }
    public required TextUnitRole Role { get; init; }
    public required string Text { get; init; }
    public string OffsetUnit { get; init; } = "textElement";
    public IReadOnlyList<int> ElementBoundariesUtf16 { get; init; } = [];
    public IReadOnlyList<TextToken> Tokens { get; init; } = [];
    public IReadOnlyList<TextSegment> Segments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Translations { get; init; } = new Dictionary<string, string>();
}

public sealed record TextToken
{
    public required Guid Id { get; init; }
    public required int Start { get; init; }
    public required int Length { get; init; }
    public required string Text { get; init; }
    public required TokenKind Kind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public PinyinSyllable? Pinyin { get; init; }
    public required AnnotationSource AnnotationSource { get; init; }
    public required bool Locked { get; init; }
    public required AnnotationReviewState ReviewState { get; init; }
    public DateTimeOffset? ModifiedAtUtc { get; init; }
}

public sealed record PinyinSyllable
{
    public required string Base { get; init; }
    public required int Tone { get; init; }
    public required bool Erhua { get; init; }
    public required string Display { get; init; }
}

public sealed record TextSegment
{
    public required Guid Id { get; init; }
    public required int Start { get; init; }
    public required int Length { get; init; }
    public required string Text { get; init; }
    public required SegmentKind Kind { get; init; }
    public required BoundarySource BoundarySource { get; init; }
    public IReadOnlyDictionary<string, string> Translations { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<Guid> TokenIds { get; init; } = [];
}

public sealed record GrammarDefinition
{
    public required IReadOnlyList<GrammarPatternPart> PatternParts { get; init; }
    public required IReadOnlyDictionary<string, string> PatternTranslations { get; init; }
    public required IReadOnlyList<Guid> ExplanationUnitIds { get; init; }
    public required IReadOnlyList<Guid> ExampleUnitIds { get; init; }
    public required IReadOnlyList<Guid> NoteUnitIds { get; init; }
    public required IReadOnlyList<string> TopicCodes { get; init; }
}

public sealed record GrammarPatternPart
{
    public required GrammarPatternPartKind Kind { get; init; }
    public required string Text { get; init; }
}

public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new UtcTimestampConverter());
        return options;
    }

    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        // Keep reading the offset form written by the initial local implementation.
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
    }
}
