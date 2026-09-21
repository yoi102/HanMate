using System.Globalization;
using System.Text;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Content;

public sealed record AnnotationPreview(ContentDocument Document, int UnmappedManualCount);

/// <summary>Conservative sentence anchors: only unchanged, unique segments retain identities.</summary>
public static class DraftAnnotation
{
    public static AnnotationPreview Generate(TextDraft draft, DeterministicPinyinCandidateEngine engine, CancellationToken cancellationToken = default)
    {
        TextDraftInput.Validate(draft);
        if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.Text)) throw new InvalidDataException("EDITOR_REQUIRED");
        if (draft.Kind == ContentKind.Word && new StringInfo(draft.Text).LengthInTextElements > 64) throw new InvalidDataException("EDITOR_WORD_LIMIT");
        var old = draft.Annotation; var now = DateTimeOffset.UtcNow;
        var role = draft.Kind switch { ContentKind.Word => TextUnitRole.Headword, ContentKind.Grammar => TextUnitRole.GrammarExplanation, _ => TextUnitRole.Body };
        var units = new List<TextUnit> { Unit(draft.Text, role) };
        GrammarDefinition? grammar = null;
        if (draft.Kind == ContentKind.Grammar)
        {
            if (string.IsNullOrWhiteSpace(draft.Example)) throw new InvalidDataException("EDITOR_GRAMMAR_REQUIRED");
            units.Add(Unit(draft.Example, TextUnitRole.Example));
            grammar = new() { PatternParts = ParsePattern(draft.Pattern), PatternTranslations = old?.Grammar?.PatternTranslations ?? new Dictionary<string, string>(),
                ExplanationUnitIds = [units[0].Id], ExampleUnitIds = [units[1].Id], NoteUnitIds = [], TopicCodes = old?.Grammar?.TopicCodes ?? [] };
        }
        var document = new ContentDocument
        {
            Id = old?.Id ?? Guid.NewGuid(), Kind = draft.Kind, Origin = ContentOrigin.Personal, Title = draft.Title,
            ContentRevision = (old?.ContentRevision ?? 0) + 1, AnnotationRevision = (old?.AnnotationRevision ?? 0) + 1,
            MetadataRevision = (old?.MetadataRevision ?? 0) + 1, CreatedAtUtc = old?.CreatedAtUtc ?? now, UpdatedAtUtc = now,
            Difficulty = old?.Difficulty, SchoolStage = old?.SchoolStage, Grade = old?.Grade, Scenes = old?.Scenes ?? [],
            Source = old?.Source ?? new() { SourceId = "personal", Type = SourceType.Imported, AuthorProvider = "User", Reference = "User input",
                LicenseIdentifier = "NOASSERTION", PermissionNotes = "Rights not verified; local personal content.", CanDistribute = false, CanShare = false,
                BackupPolicy = BackupPolicy.Allowed, ReviewStatus = ReviewStatus.Draft },
            TextUnits = units, Grammar = grammar
        };
        var validation = new ContentDocumentValidator().Validate(document);
        if (!validation.IsValid) throw new InvalidDataException(validation.Errors[0].Code);
        var retained = units.SelectMany(u => u.Tokens).Where(t => t.Locked).Select(t => t.Id).ToHashSet();
        return new(document, old?.TextUnits.SelectMany(u => u.Tokens).Count(t => t.Locked && !retained.Contains(t.Id)) ?? 0);

        TextUnit Unit(string text, TextUnitRole unitRole)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = old?.TextUnits.SingleOrDefault(u => u.Role == unitRole);
            var boundaries = TextElementMap.CreateUtf16Boundaries(text); var ranges = Split(text, boundaries);
            var tokens = new List<TextToken>(); var segments = new List<TextSegment>();
            var oldByText = previous?.Segments.GroupBy(s => s.Text, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal) ?? [];
            var newCounts = ranges.GroupBy(r => r.Text, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var oldTokens = previous?.Tokens.ToDictionary(t => t.Id) ?? [];
            var oldSegments = previous?.Segments.ToDictionary(s => s.Start) ?? [];
            var anchors = new Dictionary<int, TextSegment>();
            var mappings = MapUnchangedEdges(previous, text, boundaries).ToList();
            foreach (var range in ranges)
            {
                TextSegment? anchor = null;
                if (previous?.Text == text && oldSegments.TryGetValue(range.Start, out var exact) && exact.Text == range.Text) anchor = exact;
                else if (newCounts[range.Text] == 1 && oldByText.TryGetValue(range.Text, out var unique)) anchor = unique;
                if (anchor is null) continue;
                anchors[range.Start] = anchor;
                mappings.AddRange(anchor.TokenIds.Select(id => oldTokens[id] with { Start = oldTokens[id].Start - anchor.Start + range.Start }));
            }
            // Conflicting old or new identities are not evidence of a unique mapping.
            var mapped = mappings.GroupBy(t => t.Id).Where(g => g.Select(t => t.Start).Distinct().Count() == 1).Select(g => g.First())
                .GroupBy(t => t.Start).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
            foreach (var range in ranges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                anchors.TryGetValue(range.Start, out var anchor);
                var preserved = Enumerable.Range(range.Start, range.Length).Where(mapped.ContainsKey).Select(start => mapped[start]).Where(t => t.Start + t.Length <= range.Start + range.Length).ToArray();
                var byStart = preserved.ToDictionary(t => t.Start - range.Start);
                var locks = preserved.Where(t => (t.Locked || t.ReviewState == AnnotationReviewState.Confirmed) && t.Pinyin is not null).Select(t => new LockedPinyinRange { Start = t.Start - range.Start, Length = t.Length, Syllables = [t.Pinyin!] }).ToArray();
                var localTokens = new List<TextToken>();
                foreach (var decision in engine.Annotate(range.Text, locks, cancellationToken))
                {
                    var localMap = TextElementMap.CreateUtf16Boundaries(decision.Text);
                    for (var i = 0; i < decision.Length; i++)
                    {
                        var element = TextElementMap.Slice(decision.Text, localMap, i, 1); var start = decision.Start + i;
                        if (byStart.TryGetValue(start, out var existing) && existing.Text == element && (existing.Locked || existing.ReviewState == AnnotationReviewState.Confirmed))
                        { localTokens.Add(existing with { Start = range.Start + start }); continue; }
                        var hanzi = DeterministicPinyinCandidateEngine.IsHanziElement(element); var pinyin = decision.Selected?.Syllables[i];
                        localTokens.Add(new() { Id = existing?.Id ?? Guid.NewGuid(), Start = range.Start + start, Length = 1, Text = element,
                            Kind = hanzi ? TokenKind.Hanzi : Kind(element), Pinyin = pinyin, AnnotationSource = decision.Source, Locked = false,
                            ReviewState = hanzi && pinyin is null ? AnnotationReviewState.Unknown : decision.ReviewState });
                    }
                }
                tokens.AddRange(localTokens);
                segments.Add(new() { Id = anchor?.Id ?? Guid.NewGuid(), Start = range.Start, Length = range.Length, Text = range.Text,
                    Kind = string.IsNullOrWhiteSpace(range.Text) ? SegmentKind.Layout : SegmentKind.Speech, BoundarySource = BoundarySource.Auto,
                    Translations = anchor?.Translations ?? new Dictionary<string, string>(), TokenIds = localTokens.Select(t => t.Id).ToArray() });
            }
            return new() { Id = previous?.Id ?? Guid.NewGuid(), Role = unitRole, Text = text, ElementBoundariesUtf16 = boundaries,
                Tokens = tokens, Segments = segments, Translations = previous?.Translations ?? new Dictionary<string, string>() };
        }
    }

    private static IEnumerable<TextToken> MapUnchangedEdges(TextUnit? previous, string text, IReadOnlyList<int> boundaries)
    {
        if (previous is null || previous.Text == text) return [];
        var oldCount = previous.ElementBoundariesUtf16.Count - 1; var count = boundaries.Count - 1; var prefix = 0; var suffix = 0;
        while (prefix < Math.Min(oldCount, count) && Same(prefix, prefix)) prefix++;
        while (suffix < Math.Min(oldCount, count) - prefix && Same(oldCount - suffix - 1, count - suffix - 1)) suffix++;
        var mapped = new List<TextToken>();
        Add(0, 0, prefix); Add(oldCount - suffix, count - suffix, suffix); return mapped;
        bool Same(int oldIndex, int newIndex) => TextElementMap.Slice(previous.Text, previous.ElementBoundariesUtf16, oldIndex, 1) == TextElementMap.Slice(text, boundaries, newIndex, 1);
        void Add(int oldStart, int newStart, int length)
        {
            if (length == 0) return;
            var unchanged = TextElementMap.Slice(text, boundaries, newStart, length);
            if (!Unique(previous.Text, unchanged) || !Unique(text, unchanged)) return;
            mapped.AddRange(previous.Tokens.Where(t => t.Start >= oldStart && t.Start + t.Length <= oldStart + length)
                .Select(t => t with { Start = newStart + t.Start - oldStart }));
        }
        static bool Unique(string source, string value)
        { var start = source.IndexOf(value, StringComparison.Ordinal); return start >= 0 && source.IndexOf(value, start + 1, StringComparison.Ordinal) < 0; }
    }

    public static ContentDocument Correct(ContentDocument document, Guid tokenId, string? input)
    {
        PinyinSyllable? syllable = null;
        if (input is not null)
        {
            var trimmed = input.Trim(); var decomposed = trimmed.Normalize(NormalizationForm.FormD);
            if (!trimmed.Any(c => c is >= '0' and <= '5') && !decomposed.Any(c => c is '\u0304' or '\u0301' or '\u030c' or '\u0300'))
                throw new FormatException("EDITOR_TONE_REQUIRED");
            syllable = PinyinSyllableParser.ParseDictionarySyllable(trimmed);
        }
        var target = document.TextUnits.SelectMany(u => u.Tokens).Single(t => t.Id == tokenId);
        if (target.Kind != TokenKind.Hanzi || target.Length != 1) throw new InvalidOperationException("EDITOR_TOKEN_INVALID");
        var now = DateTimeOffset.UtcNow;
        return document with { AnnotationRevision = document.AnnotationRevision + 1, UpdatedAtUtc = now,
            TextUnits = document.TextUnits.Select(u => u with { Tokens = u.Tokens.Select(t => t.Id != tokenId ? t : t with {
                Pinyin = syllable, Locked = true, AnnotationSource = AnnotationSource.Manual, ModifiedAtUtc = now,
                ReviewState = syllable is null ? AnnotationReviewState.Unknown : AnnotationReviewState.Confirmed }).ToArray() }).ToArray() };
    }

    public static IReadOnlyList<GrammarPatternPart> ParsePattern(string pattern)
    {
        var parts = new List<GrammarPatternPart>(); var buffer = new StringBuilder(); var slot = false;
        foreach (var c in pattern)
        {
            if (c == '{') { if (slot) throw new InvalidDataException("EDITOR_PATTERN_INVALID"); Flush(); slot = true; }
            else if (c == '}') { if (!slot || buffer.Length == 0) throw new InvalidDataException("EDITOR_PATTERN_INVALID"); Flush(); slot = false; }
            else buffer.Append(c);
        }
        if (slot) throw new InvalidDataException("EDITOR_PATTERN_INVALID"); Flush();
        if (parts.Count is < 1 or > 40 || parts.Any(p => string.IsNullOrWhiteSpace(p.Text) || p.Text.EnumerateRunes().Count() > 80))
            throw new InvalidDataException("EDITOR_PATTERN_INVALID");
        return parts;
        void Flush() { if (buffer.Length > 0) { parts.Add(new() { Kind = slot ? GrammarPatternPartKind.Slot : GrammarPatternPartKind.Literal, Text = buffer.ToString() }); buffer.Clear(); } }
    }

    private static TokenKind Kind(string element)
    {
        var rune = Rune.GetRuneAt(element, 0);
        return Rune.IsWhiteSpace(rune) ? TokenKind.Whitespace : Rune.IsPunctuation(rune) ? TokenKind.Punctuation :
            Rune.IsDigit(rune) ? TokenKind.Number : Rune.IsLetter(rune) ? TokenKind.Latin : TokenKind.Symbol;
    }
    private static IReadOnlyList<(int Start, int Length, string Text)> Split(string text, IReadOnlyList<int> boundaries)
    {
        var result = new List<(int, int, string)>(); var start = 0; var count = boundaries.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var e = TextElementMap.Slice(text, boundaries, i, 1);
            if (e.Contains('\r') || e.Contains('\n')) { Add(i); start = i; Add(i + 1); }
            else if (e is "。" or "！" or "？" or ";" or "；" or "!" or "?") Add(i + 1);
        }
        Add(count); return result;
        void Add(int end) { if (end > start) result.Add((start, end - start, TextElementMap.Slice(text, boundaries, start, end - start))); start = end; }
    }
}
