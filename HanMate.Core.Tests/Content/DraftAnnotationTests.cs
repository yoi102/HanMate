using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Tests.Content;

public sealed class DraftAnnotationTests
{
    private static readonly DeterministicPinyinCandidateEngine Engine = new([
        new() { Text = "好", Syllables = [PinyinSyllableParser.ParseDictionarySyllable("hao3")], Tier = PinyinLexiconTier.CharacterDictionary, Priority = 0, SourceId = "test" },
        new() { Text = "行", Syllables = [PinyinSyllableParser.ParseDictionarySyllable("xing2")], Tier = PinyinLexiconTier.CharacterDictionary, Priority = 0, SourceId = "test" },
        new() { Text = "行", Syllables = [PinyinSyllableParser.ParseDictionarySyllable("hang2")], Tier = PinyinLexiconTier.CharacterDictionary, Priority = 0, SourceId = "test" }
    ]);
    [Fact]
    public void ProjectionPreservesExactUnicodeAndAmbiguity()
    {
        const string text = "好行。\r\n\r\ne\u0301 😀𠀀";
        var document = DraftAnnotation.Generate(new("test", text), Engine).Document;
        Assert.True(new ContentDocumentValidator().Validate(document).IsValid);
        var unit = Assert.Single(document.TextUnits);
        Assert.Equal(text, string.Concat(unit.Tokens.Select(t => t.Text)));
        Assert.Equal(text, string.Concat(unit.Segments.Select(s => s.Text)));
        Assert.Equal(2, unit.Segments.Count(s => s.Kind == SegmentKind.Layout));
        Assert.Equal(AnnotationReviewState.NeedsReview, unit.Tokens[0].ReviewState);
        Assert.Null(unit.Tokens[1].Pinyin); Assert.Equal(AnnotationReviewState.Unknown, unit.Tokens[1].ReviewState);
        Assert.Contains(unit.Tokens, t => t.Text == "e\u0301" && t.Length == 1);
        Assert.Contains(unit.Tokens, t => t.Text == "𠀀" && t.Kind == TokenKind.Hanzi);
    }
    [Theory]
    [InlineData("lv4", "lǜ")]
    [InlineData("lu:4", "lǜ")]
    [InlineData("nǐ", "nǐ")]
    [InlineData("ma5", "ma")]
    public void ManualCorrectionRequiresToneAndLocksOneOccurrence(string input, string display)
    {
        var document = DraftAnnotation.Generate(new("test", "好好"), Engine).Document;
        var changed = DraftAnnotation.Correct(document, document.TextUnits[0].Tokens[1].Id, input);
        Assert.False(changed.TextUnits[0].Tokens[0].Locked);
        Assert.Equal(display, changed.TextUnits[0].Tokens[1].Pinyin!.Display);
        Assert.True(changed.TextUnits[0].Tokens[1].Locked); Assert.Equal(AnnotationSource.Manual, changed.TextUnits[0].Tokens[1].AnnotationSource);
        Assert.True(new ContentDocumentValidator().Validate(changed).IsValid);
    }
    [Theory]
    [InlineData("nu")]
    [InlineData("ni6")]
    [InlineData("ni33")]
    [InlineData("nǐ3")]
    public void InvalidOrUntonedManualInputIsRejected(string input)
    {
        var doc = DraftAnnotation.Generate(new("t", "好"), Engine).Document;
        Assert.Throws<FormatException>(() => DraftAnnotation.Correct(doc, doc.TextUnits[0].Tokens[0].Id, input));
    }
    [Fact]
    public void UniqueUnchangedSentenceKeepsIdsAndExplicitUnknownAfterInsertion()
    {
        var doc = DraftAnnotation.Generate(new("t", "好。行。"), Engine).Document;
        var target = doc.TextUnits[0].Tokens.Single(t => t.Text == "行");
        doc = DraftAnnotation.Correct(doc, target.Id, null);
        var preview = DraftAnnotation.Generate(new("t2", "新。好。行。") { Annotation = doc }, Engine);
        Assert.Equal(0, preview.UnmappedManualCount);
        var preserved = preview.Document.TextUnits[0].Tokens.Single(t => t.Id == target.Id);
        Assert.Equal(4, preserved.Start); Assert.True(preserved.Locked); Assert.Null(preserved.Pinyin);
        Assert.Equal(doc.TextUnits[0].Segments.Last().Id, preview.Document.TextUnits[0].Segments.Last().Id);
    }
    [Fact]
    public void RepeatedChangedSentencesDoNotGuessManualAnchor()
    {
        var doc = DraftAnnotation.Generate(new("t", "好。好。"), Engine).Document;
        doc = DraftAnnotation.Correct(doc, doc.TextUnits[0].Tokens[0].Id, "hao4");
        var same = DraftAnnotation.Generate(new("renamed", "好。好。") { Annotation = doc }, Engine);
        Assert.Equal(0, same.UnmappedManualCount); Assert.True(same.Document.TextUnits[0].Tokens[0].Locked);
        var ambiguous = DraftAnnotation.Generate(new("t", "好。好。好。") { Annotation = doc }, Engine);
        Assert.Equal(1, ambiguous.UnmappedManualCount); Assert.DoesNotContain(ambiguous.Document.TextUnits[0].Tokens, t => t.Locked);
    }
    [Fact]
    public void UniqueUnchangedSuffixWithinEditedSentenceRetainsManualTokens()
    {
        var doc = DraftAnnotation.Generate(new("t", "我好行。"), Engine).Document;
        var token = doc.TextUnits[0].Tokens[2]; doc = DraftAnnotation.Correct(doc, token.Id, "hang2");
        var result = DraftAnnotation.Generate(new("t", "现在我好行。") { Annotation = doc }, Engine);
        Assert.Equal(0, result.UnmappedManualCount);
        var retained = result.Document.TextUnits[0].Tokens.Single(t => t.Id == token.Id);
        Assert.Equal(4, retained.Start); Assert.True(retained.Locked); Assert.Equal("háng", retained.Pinyin!.Display);
        Assert.NotEqual(doc.TextUnits[0].Segments[0].Id, result.Document.TextUnits[0].Segments[0].Id);
    }
    [Fact]
    public void GrammarRequiresStructuredPatternAndExampleAndNeverTokenizesSlots()
    {
        var draft = new TextDraft("是", "说明。", ContentKind.Grammar) { Pattern = "{主语} 是 {名词}", Example = "我是学生。" };
        var doc = DraftAnnotation.Generate(draft, Engine).Document;
        Assert.True(new ContentDocumentValidator().Validate(doc).IsValid);
        Assert.Equal(2, doc.TextUnits.Count); Assert.Equal(2, doc.Grammar!.PatternParts.Count(p => p.Kind == GrammarPatternPartKind.Slot));
        Assert.DoesNotContain(doc.TextUnits.SelectMany(u => u.Tokens), t => t.Text == "主");
        Assert.Throws<InvalidDataException>(() => DraftAnnotation.Generate(draft with { Example = "" }, Engine));
        Assert.Throws<InvalidDataException>(() => DraftAnnotation.Generate(draft with { Pattern = "{oops" }, Engine));
    }
    [Fact]
    public void CancellationAndLimitsLeaveInputUnchanged()
    {
        var draft = new TextDraft("t", new string('好', 20000)); using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => DraftAnnotation.Generate(draft, Engine, cts.Token));
        Assert.Null(draft.Annotation);
        Assert.Throws<InvalidDataException>(() => DraftAnnotation.Generate(draft with { Text = draft.Text + "好" }, Engine));
        Assert.Throws<InvalidDataException>(() => DraftAnnotation.Generate(new("t", new string('好', 65), ContentKind.Word), Engine));
    }
}
