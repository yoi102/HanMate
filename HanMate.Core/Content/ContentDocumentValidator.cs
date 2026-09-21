using System.Globalization;

namespace HanMate.Core.Content;

public sealed record ContentValidationError(string Code, string Path, string Message);

public sealed record ContentValidationResult(IReadOnlyList<ContentValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class ContentDocumentValidator
{
    public ContentValidationResult Validate(ContentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<ContentValidationError>();
        var ids = new HashSet<Guid>();

        Check(document.SchemaVersion == 2, "CONTENT_VERSION_UNSUPPORTED", "schemaVersion", "Only schema version 2 is supported.");
        AddId(document.Id, "id");
        Check(!string.IsNullOrWhiteSpace(document.Title), "CONTENT_TITLE_REQUIRED", "title", "Title cannot be blank.");
        Check(new StringInfo(document.Title).LengthInTextElements <= 120, "CONTENT_TITLE_LIMIT", "title", "Title exceeds 120 text elements.");
        Check(document.ContentRevision >= 1 && document.AnnotationRevision >= 1 && document.MetadataRevision >= 1,
            "CONTENT_REVISION_INVALID", "revisions", "All content revisions must be positive.");
        Check(document.CreatedAtUtc.Offset == TimeSpan.Zero && document.UpdatedAtUtc.Offset == TimeSpan.Zero,
            "CONTENT_TIME_NOT_UTC", "timestamps", "Persistent timestamps must use UTC.");
        Check(document.Scenes.Count == document.Scenes.Distinct(StringComparer.Ordinal).Count(),
            "CONTENT_SCENE_DUPLICATE", "scenes", "Scene codes must be unique.");
        CheckGrade(document, errors);
        errors.AddRange(GrammarValidator.Validate(document).Errors);
        if (document.TextUnits is null || document.TextUnits.Any(unit => unit is null || unit.Text is null || unit.Tokens is null || unit.Segments is null || unit.ElementBoundariesUtf16 is null))
        {
            errors.Add(new("CONTENT_STRUCTURE_INVALID", "textUnits", "Text units and their range collections cannot be null."));
            return new(errors);
        }

        var totalElements = 0;
        for (var unitIndex = 0; unitIndex < document.TextUnits.Count; unitIndex++)
        {
            var unit = document.TextUnits[unitIndex];
            var path = $"textUnits[{unitIndex}]";
            AddId(unit.Id, $"{path}.id");
            totalElements += ValidateUnit(unit, path, ids, errors);
        }

        Check(totalElements <= 20_000, "CONTENT_TEXT_LIMIT", "textUnits", "Content exceeds 20,000 text elements.");
        ValidateRoles(document, errors);
        return new ContentValidationResult(errors);

        void AddId(Guid id, string path)
        {
            Check(id != Guid.Empty, "CONTENT_ID_EMPTY", path, "Stable IDs cannot be empty.");
            if (id != Guid.Empty) Check(ids.Add(id), "CONTENT_ID_DUPLICATE", path, "Stable IDs must be unique within a document.");
        }

        void Check(bool condition, string code, string path, string message)
        {
            if (!condition) errors.Add(new ContentValidationError(code, path, message));
        }
    }

    private static int ValidateUnit(TextUnit unit, string path, HashSet<Guid> ids, List<ContentValidationError> errors)
    {
        var expectedBoundaries = TextElementMap.CreateUtf16Boundaries(unit.Text);
        Add(expectedBoundaries.SequenceEqual(unit.ElementBoundariesUtf16), "TEXT_BOUNDARY_MISMATCH", $"{path}.elementBoundariesUtf16",
            "The persisted UTF-16 boundary map does not match the original text.");
        Add(unit.OffsetUnit == "textElement", "TEXT_OFFSET_UNIT_INVALID", $"{path}.offsetUnit", "Ranges must use text elements.");
        var elementCount = expectedBoundaries.Count - 1;

        var tokenPosition = 0;
        var tokenIds = new HashSet<Guid>();
        for (var index = 0; index < unit.Tokens.Count; index++)
        {
            var token = unit.Tokens[index];
            var tokenPath = $"{path}.tokens[{index}]";
            AddUniqueId(token.Id, $"{tokenPath}.id");
            tokenIds.Add(token.Id);
            Add(token.Start == tokenPosition && token.Length > 0 && token.Start + token.Length <= elementCount,
                "TEXT_TOKEN_RANGE_INVALID", tokenPath, "Tokens must be positive, contiguous, and within the unit.");
            if (token.Start >= 0 && token.Length >= 0 && token.Start + token.Length <= elementCount)
            {
                Add(TextElementMap.Slice(unit.Text, expectedBoundaries, token.Start, token.Length) == token.Text,
                    "TEXT_TOKEN_SLICE_MISMATCH", $"{tokenPath}.text", "Token text does not match its range.");
            }
            tokenPosition = token.Start + token.Length;
            ValidatePinyin(token, tokenPath, errors);
        }
        Add(tokenPosition == elementCount, "TEXT_TOKEN_COVERAGE_INVALID", $"{path}.tokens", "Tokens must cover the original text exactly.");

        var segmentPosition = 0;
        var referencedTokens = new List<Guid>();
        for (var index = 0; index < unit.Segments.Count; index++)
        {
            var segment = unit.Segments[index];
            var segmentPath = $"{path}.segments[{index}]";
            AddUniqueId(segment.Id, $"{segmentPath}.id");
            Add(segment.Start == segmentPosition && segment.Length > 0 && segment.Start + segment.Length <= elementCount,
                "TEXT_SEGMENT_RANGE_INVALID", segmentPath, "Segments must be positive, contiguous, and within the unit.");
            if (segment.Start >= 0 && segment.Length >= 0 && segment.Start + segment.Length <= elementCount)
            {
                Add(TextElementMap.Slice(unit.Text, expectedBoundaries, segment.Start, segment.Length) == segment.Text,
                    "TEXT_SEGMENT_SLICE_MISMATCH", $"{segmentPath}.text", "Segment text does not match its range.");
            }
            Add(segment.TokenIds.All(tokenIds.Contains), "TEXT_SEGMENT_TOKEN_MISSING", $"{segmentPath}.tokenIds", "Segment references an unknown token.");
            referencedTokens.AddRange(segment.TokenIds);
            segmentPosition = segment.Start + segment.Length;
        }
        Add(segmentPosition == elementCount, "TEXT_SEGMENT_COVERAGE_INVALID", $"{path}.segments", "Segments must cover the original text exactly.");
        Add(referencedTokens.SequenceEqual(unit.Tokens.Select(token => token.Id)), "TEXT_SEGMENT_TOKEN_ORDER_INVALID", $"{path}.segments",
            "Segments must reference each token once in document order.");
        return elementCount;

        void AddUniqueId(Guid id, string idPath)
        {
            Add(id != Guid.Empty, "CONTENT_ID_EMPTY", idPath, "Stable IDs cannot be empty.");
            if (id != Guid.Empty) Add(ids.Add(id), "CONTENT_ID_DUPLICATE", idPath, "Stable IDs must be unique within a document.");
        }

        void Add(bool condition, string code, string errorPath, string message)
        {
            if (!condition) errors.Add(new ContentValidationError(code, errorPath, message));
        }
    }

    private static void ValidatePinyin(TextToken token, string path, List<ContentValidationError> errors)
    {
        void Add(bool condition, string code, string suffix, string message)
        {
            if (!condition) errors.Add(new ContentValidationError(code, $"{path}.{suffix}", message));
        }

        if (token.Pinyin is null)
        {
            Add(token.Kind != TokenKind.Hanzi || token.ReviewState == AnnotationReviewState.Unknown,
                "PINYIN_UNKNOWN_STATE_INVALID", "reviewState", "A Hanzi token without pinyin must be marked unknown.");
            return;
        }

        Add(token.Kind == TokenKind.Hanzi, "PINYIN_NON_HANZI", "pinyin", "Only Hanzi tokens may carry pinyin.");
        Add(token.Pinyin.Tone is >= 0 and <= 4, "PINYIN_TONE_INVALID", "pinyin.tone", "Tone must be 0 through 4.");
        Add(token.Pinyin.Base == token.Pinyin.Base.ToLowerInvariant(), "PINYIN_BASE_INVALID", "pinyin.base", "Pinyin base must be lowercase.");
        try
        {
            Add(PinyinFormatter.Format(token.Pinyin.Base, token.Pinyin.Tone, token.Pinyin.Erhua) == token.Pinyin.Display,
                "PINYIN_DISPLAY_MISMATCH", "pinyin.display", "Display pinyin does not match base, tone, and erhua.");
        }
        catch (ArgumentException)
        {
            Add(false, "PINYIN_BASE_INVALID", "pinyin.base", "Pinyin base is not a supported syllable form.");
        }
    }

    private static void ValidateRoles(ContentDocument document, List<ContentValidationError> errors)
    {
        var roles = document.TextUnits.Select(unit => unit.Role).ToList();
        void Add(bool condition, string code, string path, string message)
        {
            if (!condition) errors.Add(new ContentValidationError(code, path, message));
        }

        switch (document.Kind)
        {
            case ContentKind.Word:
                Add(roles.Count(role => role == TextUnitRole.Headword) == 1 && roles.All(role => role is TextUnitRole.Headword or TextUnitRole.Definition or TextUnitRole.Example),
                    "CONTENT_ROLE_INVALID", "textUnits", "A word requires one headword and only word roles.");
                break;
            case ContentKind.Text:
            case ContentKind.Poem:
                Add(roles.SequenceEqual([TextUnitRole.Body]), "CONTENT_ROLE_INVALID", "textUnits", "Text and poem content require exactly one body unit.");
                break;
            case ContentKind.Grammar:
                // Grammar-specific validation is shared with the editor/import boundary.
                break;
        }
    }

    private static void CheckGrade(ContentDocument document, List<ContentValidationError> errors)
    {
        var valid = document.SchoolStage is null
            ? document.Grade is null
            : document.Grade is null || document.Grade >= 1 && document.Grade <= (document.SchoolStage == SchoolStage.Primary ? 6 : 3);
        if (!valid) errors.Add(new ContentValidationError("CONTENT_GRADE_INVALID", "grade", "Grade does not match the school stage."));
    }
}
