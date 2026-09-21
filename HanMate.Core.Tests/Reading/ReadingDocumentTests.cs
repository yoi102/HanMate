using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Reading;

namespace HanMate.Core.Tests.Reading;

public sealed class ReadingDocumentTests
{
    [Fact]
    public void LessonRowsPreserveTextPoemLineBreaksAndExactSelectedScopes()
    {
        var contents = JsonSerializer.Deserialize<ContentCollection>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/contents.json")), ContentJson.Options)!;
        foreach (var content in contents.Contents.Where(c => c.Kind is ContentKind.Text or ContentKind.Poem))
        {
            var before = JsonSerializer.Serialize(content, ContentJson.Options);
            var reading = new ReadingDocument(content);
            var full = LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.English);
            Assert.Equal(string.Concat(content.TextUnits.Select(u => u.Text)), string.Concat(full.SelectMany(b => b.Atoms).Select(a => a.Text)));
            Assert.All(full, b => Assert.InRange(b.Atoms.Count, 0, ReadingDocument.PageAtomLimit));
            foreach (var block in full.Where(b => b.Target is not null))
                Assert.All(block.Atoms, atom => Assert.Equal(block.Target, reading.TargetFor(atom)));
            if (content.Kind == ContentKind.Poem) Assert.Contains(full, b => b.Target is null && b.Atoms.Any(a => a.HardBreak));
            foreach (var target in reading.Targets)
            {
                var focused = LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.English, target);
                Assert.Equal(reading.Segment(target)!.Text, string.Concat(focused.SelectMany(b => b.Atoms).Select(a => a.Text)));
                Assert.All(focused, b => Assert.Equal(target, b.Target));
                if (reading.Segment(target)!.Translations.Count == 0) Assert.All(focused, b => Assert.Null(b.Translation));
            }
            Assert.All(LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.ChineseSimplified), b => Assert.Null(b.Translation));
            Assert.Equal(before, JsonSerializer.Serialize(content, ContentJson.Options));
        }
    }

    [Fact]
    public void LongLessonSegmentStaysBoundedAndTranslationAppearsOnlyAfterItsLastChunk()
    {
        var content = Text(new string('中', 300), annotated: true);
        var unit = content.TextUnits[0];
        unit = unit with { Translations = new Dictionary<string, string> { ["en"] = "Whole paragraph" },
            Segments = [unit.Segments[0] with { Translations = new Dictionary<string, string> { ["en"] = "This segment", ["ja"] = "この部分" } }] };
        var reading = new ReadingDocument(content with { TextUnits = [unit] });
        var target = reading.Targets.Single();
        var focused = LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.English, target);
        Assert.Equal(4, focused.Count); Assert.All(focused, b => Assert.Equal(target, b.Target));
        Assert.All(focused.Take(3), b => Assert.Null(b.Translation));
        Assert.Equal("This segment", focused[^1].Translation);
        Assert.Equal("この部分", LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.Japanese, target)[^1].Translation);
        Assert.Equal("Whole paragraph", LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.English)[^1].Translation);
        Assert.All(focused, b => Assert.InRange(b.Atoms.Count, 1, ReadingDocument.PageAtomLimit));
        Assert.Throws<ArgumentException>(() => LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.English, new(unit.Id, Guid.NewGuid())));
    }

    private static ContentDocument Template() => JsonSerializer.Deserialize<ContentDocument>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/dictionary-entry.json")), ContentJson.Options)!;
    private static ContentDocument Text(string text, bool annotated = false)
    {
        var map = TextElementMap.CreateUtf16Boundaries(text);
        var tokens = Enumerable.Range(0, map.Count - 1).Select(i => new TextToken
        {
            Id = Guid.NewGuid(), Start = i, Length = 1, Text = TextElementMap.Slice(text, map, i, 1), Kind = annotated ? TokenKind.Hanzi : TokenKind.Symbol,
            Pinyin = annotated ? new PinyinSyllable { Base = "zhong", Tone = 1, Erhua = false, Display = "zhōng" } : null,
            AnnotationSource = annotated ? AnnotationSource.Manual : AnnotationSource.None, Locked = false,
            ReviewState = annotated ? AnnotationReviewState.Confirmed : AnnotationReviewState.NotApplicable
        }).ToArray();
        return Template() with { Kind = ContentKind.Text, Grammar = null, TextUnits = [new TextUnit
        {
            Id = Guid.NewGuid(), Role = TextUnitRole.Body, Text = text, ElementBoundariesUtf16 = map, Tokens = tokens,
            Segments = [new TextSegment { Id = Guid.NewGuid(), Start = 0, Length = map.Count - 1, Text = text, Kind = SegmentKind.Speech, BoundarySource = BoundarySource.Manual, TokenIds = tokens.Select(t => t.Id).ToArray() }]
        }] };
    }

    [Fact]
    public void TwentyThousandAnnotatedCharactersStayCompleteWithBoundedNativePages()
    {
        var content = Text(new string('中', 20000), annotated: true); var original = JsonSerializer.Serialize(content, ContentJson.Options);
        var reading = new ReadingDocument(content); var atoms = reading.Pages.SelectMany(p => p.Parts).SelectMany(p => p.Atoms).ToArray();
        Assert.Equal(209, reading.Pages.Count); Assert.Equal(20000, atoms.Length);
        Assert.Equal(content.TextUnits[0].Text, string.Concat(atoms.Select(a => a.Text)));
        Assert.All(reading.Pages, page => Assert.InRange(page.Parts.Sum(p => p.Atoms.Count), 1, ReadingDocument.PageAtomLimit));
        Assert.All(atoms, a => Assert.Equal("zhōng", a.Pinyin)); Assert.Single(atoms.Select(a => a.SegmentId).Distinct());
        Assert.Equal(original, JsonSerializer.Serialize(content, ContentJson.Options));
        Assert.Equal(209, reading.PagesFor(reading.Targets.Single()).Count);
    }

    [Fact]
    public void MixedTextPreservesCombiningMarksEmojiCrLfAndBlankLines()
    {
        const string text = "n\u030C / 👨‍👩‍👧‍👦 / 3.14\r\n\r\n「你好！」\n";
        var content = Text(text); var reading = new ReadingDocument(content);
        var atoms = reading.Pages.SelectMany(p => p.Parts).SelectMany(p => p.Atoms).ToArray();
        Assert.Equal(text, string.Concat(atoms.Select(a => a.Text)));
        Assert.Contains(atoms, a => a.Text == "n\u030C"); Assert.Contains(atoms, a => a.Text == "👨‍👩‍👧‍👦");
        Assert.Equal(3, atoms.Count(a => a.HardBreak)); Assert.Equal(2, atoms.Count(a => a.Text == "\r\n"));
        foreach (var atom in atoms) Assert.Equal(atom.Text, TextElementMap.Slice(text, content.TextUnits[0].ElementBoundariesUtf16, atom.Start, atom.Length));
    }

    [Fact]
    public void UnannotatedLongTokenSplitsOnlyAtGraphemeBoundaries()
    {
        var content = Text(new string('x', 300) + "👩‍💻e\u0301"); var unit = content.TextUnits[0];
        var token = unit.Tokens[0] with { Text = unit.Text, Length = unit.ElementBoundariesUtf16.Count - 1, Kind = TokenKind.Latin };
        unit = unit with { Tokens = [token], Segments = [unit.Segments[0] with { TokenIds = [token.Id] }] };
        var reading = new ReadingDocument(content with { TextUnits = [unit] });
        var atoms = reading.Pages.SelectMany(p => p.Parts).SelectMany(p => p.Atoms).ToArray();
        Assert.Equal(4, reading.Pages.Count); Assert.Equal(unit.Text, string.Concat(atoms.Select(a => a.Text)));
        Assert.Single(atoms.Select(a => a.TokenId).Distinct()); Assert.Contains(atoms, a => a.Text == "👩‍💻");
    }

    [Fact]
    public void SelectedSpeechScopeUsesPersistedIdsAndDoesNotIncludeAdjacentText()
    {
        var content = Text("甲，乙。"); var unit = content.TextUnits[0];
        var first = unit.Segments[0] with { Length = 2, Text = "甲，", TokenIds = unit.Tokens.Take(2).Select(t => t.Id).ToArray() };
        var second = first with { Id = Guid.NewGuid(), Start = 2, Text = "乙。", TokenIds = unit.Tokens.Skip(2).Select(t => t.Id).ToArray() };
        var reading = new ReadingDocument(content with { TextUnits = [unit with { Segments = [first, second] }] });
        Assert.Equal(2, reading.Targets.Count);
        var selected = reading.PagesFor(new(unit.Id, second.Id)).SelectMany(p => p.Parts).SelectMany(p => p.Atoms).ToArray();
        Assert.Equal("乙。", string.Concat(selected.Select(a => a.Text))); Assert.Equal(2, selected[0].Start);
        Assert.All(selected, a => Assert.Equal(new(unit.Id, second.Id), reading.TargetFor(a)));
        Assert.Throws<ArgumentException>(() => reading.PagesFor(new(unit.Id, Guid.NewGuid())));
    }

    [Fact]
    public void EveryExistingKindRendersAllUnitsAndGrammarExamplesKeepWholeUnitTargets()
    {
        var collection = JsonSerializer.Deserialize<ContentCollection>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/contents.json")), ContentJson.Options)!;
        foreach (var content in collection.Contents)
        {
            var reading = new ReadingDocument(content);
            foreach (var unit in content.TextUnits)
                Assert.Equal(unit.Text, string.Concat(reading.Pages.SelectMany(p => p.Parts).Where(p => p.Unit.Id == unit.Id).SelectMany(p => p.Atoms).Select(a => a.Text)));
            if (content.Kind is ContentKind.Word or ContentKind.Grammar)
                Assert.All(reading.Targets, target => Assert.Null(target.SegmentId));
        }
    }

    [Fact]
    public void WrappedRubyPairsAndClosingPunctuationStayTogetherAtNormalAndDoubleScale()
    {
        var reading = new ReadingDocument(Text("中中中，中"));
        var atoms = reading.Pages[0].Parts[0].Atoms;
        foreach (var scale in new[] { 1d, 2d })
        {
            var layout = RubyLineLayout.Arrange(atoms.Select(a => new RubyMeasure(a, 10 * scale, 20 * scale)).ToArray(), 30 * scale, 20 * scale);
            Assert.Equal(layout.Placements[2].Y, layout.Placements[3].Y);
            Assert.Equal(0, layout.Placements[2].X); Assert.Equal(10 * scale, layout.Placements[3].X);
            Assert.All(layout.Placements, p => Assert.True(p.X + p.Width <= 30 * scale));
        }
    }

    [Theory]
    [InlineData("甲\n乙", 2)]
    [InlineData("甲\r\n\r\n乙", 3)]
    [InlineData("甲\n", 2)]
    [InlineData("\n", 2)]
    [InlineData("甲\n\n", 3)]
    public void HardBreaksPreservePoemLinesIncludingFinalEmptyLine(string text, int expectedLines)
    {
        var atoms = new ReadingDocument(Text(text)).Pages.SelectMany(p => p.Parts).SelectMany(p => p.Atoms).ToArray();
        var layout = RubyLineLayout.Arrange(atoms.Select(a => new RubyMeasure(a, a.HardBreak ? 0 : 10, 20)).ToArray(), 300, 20);
        Assert.Equal(expectedLines * 20, layout.Height); Assert.Equal(atoms.Length, layout.Placements.Count);
    }

    [Fact]
    public void UnknownChineseAnnotationIsDistinctFromUnannotatedSymbols()
    {
        var content = Text("中!"); var unit = content.TextUnits[0];
        unit = unit with { Tokens = [unit.Tokens[0] with { Kind = TokenKind.Hanzi, ReviewState = AnnotationReviewState.Unknown, AnnotationSource = AnnotationSource.Unknown }, unit.Tokens[1]] };
        var atoms = new ReadingDocument(content with { TextUnits = [unit] }).Pages[0].Parts[0].Atoms;
        Assert.True(atoms[0].MissingPinyin); Assert.False(atoms[1].MissingPinyin); Assert.Null(atoms[0].Pinyin);
    }

    [Fact]
    public void NarrowContainersMakeProgressAndBadDimensionsAreRejected()
    {
        var atoms = new ReadingDocument(Text("ABC中")).Pages[0].Parts[0].Atoms;
        var metrics = atoms.Select(a => new RubyMeasure(a, 30, 20)).ToArray();
        var layout = RubyLineLayout.Arrange(metrics, 1, 20); Assert.Equal(4, layout.Placements.Count); Assert.Equal(80, layout.Height);
        Assert.Throws<ArgumentOutOfRangeException>(() => RubyLineLayout.Arrange(metrics, double.NaN, 20));
    }
}
