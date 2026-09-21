using System.Text;
using HanMate.Core.Content;

namespace HanMate.Core.Pinyin;

public enum PinyinLexiconTier
{
    ReviewedPhrase = 0,
    PhraseDictionary = 1,
    CharacterDictionary = 2
}

public enum PinyinDecisionReason
{
    LockedManual,
    ReviewedPhrase,
    LongestPhrase,
    CharacterFallback,
    AmbiguousAtBestRank,
    Unknown,
    NonHanzi
}

public sealed record PinyinLexiconEntry
{
    public required string Text { get; init; }
    public required IReadOnlyList<PinyinSyllable> Syllables { get; init; }
    public required PinyinLexiconTier Tier { get; init; }
    public required int Priority { get; init; }
    public required string SourceId { get; init; }
}

public sealed record LockedPinyinRange
{
    public required int Start { get; init; }
    public required int Length { get; init; }
    public required IReadOnlyList<PinyinSyllable> Syllables { get; init; }
}

public sealed record PinyinCandidateSequence
{
    public required IReadOnlyList<PinyinSyllable> Syllables { get; init; }
    public required string SourceId { get; init; }
    public required int Priority { get; init; }
}

public sealed record PinyinAnnotationSegment
{
    public required int Start { get; init; }
    public required int Length { get; init; }
    public required string Text { get; init; }
    public required AnnotationSource Source { get; init; }
    public required AnnotationReviewState ReviewState { get; init; }
    public required PinyinDecisionReason Reason { get; init; }
    public PinyinCandidateSequence? Selected { get; init; }
    public IReadOnlyList<PinyinCandidateSequence> Candidates { get; init; } = [];
}

public sealed class DeterministicPinyinCandidateEngine
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<CompiledEntry>> _entriesByFirstElement;

    public DeterministicPinyinCandidateEngine(IEnumerable<PinyinLexiconEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entriesByFirstElement = entries
            .Select(CompileAndValidate)
            .GroupBy(entry => entry.FirstElement, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CompiledEntry>)group
                    .OrderBy(entry => entry.Entry.Tier)
                    .ThenByDescending(entry => entry.ElementLength)
                    .ThenBy(entry => entry.Entry.Priority)
                    .ThenBy(entry => ReadingKey(entry.Entry.Syllables), StringComparer.Ordinal)
                    .ThenBy(entry => entry.Entry.SourceId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<PinyinAnnotationSegment> Annotate(string text, IReadOnlyList<LockedPinyinRange>? lockedRanges = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var boundaries = TextElementMap.CreateUtf16Boundaries(text);
        var elementCount = boundaries.Count - 1;
        var locks = ValidateLocks(text, boundaries, lockedRanges ?? []);
        var lockByStart = locks.ToDictionary(item => item.Start);
        var segments = new List<PinyinAnnotationSegment>();
        var lockCursor = 0;

        for (var index = 0; index < elementCount;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lockByStart.TryGetValue(index, out var locked))
            {
                var selected = new PinyinCandidateSequence
                {
                    Syllables = locked.Syllables,
                    SourceId = "manual",
                    Priority = 0
                };
                segments.Add(CreateSegment(text, boundaries, index, locked.Length, AnnotationSource.Manual,
                    AnnotationReviewState.Confirmed, PinyinDecisionReason.LockedManual, selected, [selected]));
                index += locked.Length;
                continue;
            }

            var element = TextElementMap.Slice(text, boundaries, index, 1);
            if (!IsHanziElement(element))
            {
                segments.Add(CreateSegment(text, boundaries, index, 1, AnnotationSource.None,
                    AnnotationReviewState.NotApplicable, PinyinDecisionReason.NonHanzi, null, []));
                index++;
                continue;
            }

            while (lockCursor < locks.Count && locks[lockCursor].Start <= index) lockCursor++;
            var nextLockStart = lockCursor < locks.Count ? locks[lockCursor].Start : elementCount;
            var matches = FindMatches(text, boundaries, index, nextLockStart, element);
            if (matches.Count == 0)
            {
                segments.Add(CreateSegment(text, boundaries, index, 1, AnnotationSource.Unknown,
                    AnnotationReviewState.Unknown, PinyinDecisionReason.Unknown, null, []));
                index++;
                continue;
            }

            var bestTier = matches.Min(item => item.Entry.Tier);
            var tierMatches = matches.Where(item => item.Entry.Tier == bestTier).ToArray();
            var bestLength = tierMatches.Max(item => item.ElementLength);
            var lengthMatches = tierMatches.Where(item => item.ElementLength == bestLength).ToArray();
            var bestPriority = lengthMatches.Min(item => item.Entry.Priority);
            var winners = lengthMatches.Where(item => item.Entry.Priority == bestPriority).ToArray();
            var candidates = winners
                .GroupBy(item => ReadingKey(item.Entry.Syllables), StringComparer.Ordinal)
                .Select(group => group.First().Entry)
                .OrderBy(entry => ReadingKey(entry.Syllables), StringComparer.Ordinal)
                .ThenBy(entry => entry.SourceId, StringComparer.Ordinal)
                .Select(entry => new PinyinCandidateSequence
                {
                    Syllables = entry.Syllables,
                    SourceId = entry.SourceId,
                    Priority = entry.Priority
                })
                .ToArray();

            var ambiguous = candidates.Length > 1;
            var source = bestTier switch
            {
                PinyinLexiconTier.ReviewedPhrase => AnnotationSource.Reviewed,
                PinyinLexiconTier.PhraseDictionary => AnnotationSource.PhraseDictionary,
                _ => AnnotationSource.CharDictionary
            };
            var reason = ambiguous
                ? PinyinDecisionReason.AmbiguousAtBestRank
                : bestTier switch
                {
                    PinyinLexiconTier.ReviewedPhrase => PinyinDecisionReason.ReviewedPhrase,
                    PinyinLexiconTier.PhraseDictionary => PinyinDecisionReason.LongestPhrase,
                    _ => PinyinDecisionReason.CharacterFallback
                };
            var reviewState = ambiguous
                ? AnnotationReviewState.NeedsReview
                : bestTier == PinyinLexiconTier.ReviewedPhrase
                    ? AnnotationReviewState.Confirmed
                    : AnnotationReviewState.NeedsReview;

            segments.Add(CreateSegment(text, boundaries, index, bestLength, source, reviewState, reason,
                ambiguous ? null : candidates[0], candidates));
            index += bestLength;
        }

        return segments;
    }

    private IReadOnlyList<CompiledEntry> FindMatches(
        string text,
        IReadOnlyList<int> boundaries,
        int start,
        int exclusiveEnd,
        string firstElement)
    {
        if (!_entriesByFirstElement.TryGetValue(firstElement, out var entries)) return [];
        return entries
            .Where(entry => start + entry.ElementLength <= exclusiveEnd)
            .Where(entry => string.Equals(
                entry.Entry.Text,
                TextElementMap.Slice(text, boundaries, start, entry.ElementLength),
                StringComparison.Ordinal))
            .ToArray();
    }

    private static CompiledEntry CompileAndValidate(PinyinLexiconEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.SourceId);
        if (entry.Priority < 0) throw new ArgumentOutOfRangeException(nameof(entry.Priority));
        if (entry.Syllables.Count == 0) throw new ArgumentException("A lexicon reading must contain syllables.", nameof(entry));

        var boundaries = TextElementMap.CreateUtf16Boundaries(entry.Text);
        var elementCount = boundaries.Count - 1;
        if (elementCount != entry.Syllables.Count)
            throw new ArgumentException("Lexicon text elements and syllables must have the same count.", nameof(entry));
        if (entry.Tier == PinyinLexiconTier.CharacterDictionary && elementCount != 1)
            throw new ArgumentException("Character dictionary entries must contain one text element.", nameof(entry));
        for (var index = 0; index < elementCount; index++)
            if (!IsHanziElement(TextElementMap.Slice(entry.Text, boundaries, index, 1)))
                throw new ArgumentException("Lexicon entries may contain only Hanzi text elements.", nameof(entry));

        return new CompiledEntry(entry, elementCount, TextElementMap.Slice(entry.Text, boundaries, 0, 1));
    }

    private static IReadOnlyList<LockedPinyinRange> ValidateLocks(
        string text,
        IReadOnlyList<int> boundaries,
        IReadOnlyList<LockedPinyinRange> locks)
    {
        var ordered = locks.OrderBy(item => item.Start).ToArray();
        var elementCount = boundaries.Count - 1;
        var end = 0;
        foreach (var item in ordered)
        {
            if (item.Start < end || item.Start < 0 || item.Length <= 0 || item.Start + item.Length > elementCount)
                throw new ArgumentException("Locked pinyin ranges must be positive, in bounds and non-overlapping.", nameof(locks));
            if (item.Syllables.Count != item.Length)
                throw new ArgumentException("Locked pinyin ranges must map one syllable to each text element.", nameof(locks));
            for (var index = item.Start; index < item.Start + item.Length; index++)
                if (!IsHanziElement(TextElementMap.Slice(text, boundaries, index, 1)))
                    throw new ArgumentException("Locked pinyin ranges may contain only Hanzi text elements.", nameof(locks));
            end = item.Start + item.Length;
        }

        return ordered;
    }

    private static PinyinAnnotationSegment CreateSegment(
        string text,
        IReadOnlyList<int> boundaries,
        int start,
        int length,
        AnnotationSource source,
        AnnotationReviewState reviewState,
        PinyinDecisionReason reason,
        PinyinCandidateSequence? selected,
        IReadOnlyList<PinyinCandidateSequence> candidates) => new()
        {
            Start = start,
            Length = length,
            Text = TextElementMap.Slice(text, boundaries, start, length),
            Source = source,
            ReviewState = reviewState,
            Reason = reason,
            Selected = selected,
            Candidates = candidates
        };

    public static bool IsHanziElement(string element)
    {
        if (!Rune.TryGetRuneAt(element, 0, out var rune) || rune.Utf16SequenceLength != element.Length) return false;
        var value = rune.Value;
        return value is >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xF900 and <= 0xFAFF
            or >= 0x20000 and <= 0x2EBEF
            or >= 0x2EBF0 and <= 0x2EE5F
            or >= 0x2F800 and <= 0x2FA1F
            or >= 0x30000 and <= 0x3347F;
    }

    private static string ReadingKey(IEnumerable<PinyinSyllable> syllables) => string.Join('|', syllables.Select(
        syllable => $"{syllable.Base}/{syllable.Tone}/{(syllable.Erhua ? 1 : 0)}"));

    private sealed record CompiledEntry(PinyinLexiconEntry Entry, int ElementLength, string FirstElement);
}
