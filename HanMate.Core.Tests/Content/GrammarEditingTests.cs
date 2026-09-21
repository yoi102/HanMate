using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Tests.Content;

public sealed class GrammarEditingTests
{
    private static readonly DeterministicPinyinCandidateEngine Engine = new([]);
    private static ContentDocument Grammar()
    {
        var doc = DraftAnnotation.Generate(new("grammar", "说明", ContentKind.Grammar) { Pattern = "{S}是{N}", Example = "你好。" }, Engine).Document;
        return DraftAnnotation.Correct(doc, doc.TextUnits[1].Tokens[0].Id, "ni3");
    }
    [Fact]
    public void ReorderAddAndTranslatePreservesUntouchedIdsLocksAndOperators()
    {
        var doc = Grammar(); var input = GrammarEditing.From(doc); var example = doc.TextUnits[1];
        var extra = new GrammarUnitInput(Guid.NewGuid(), TextUnitRole.Example, "另一例句", new Dictionary<string, string> { ["en"] = "Another example" });
        input = input with { Units = [extra, .. input.Units.Reverse()], Parts = [.. input.Parts, new() { Kind = GrammarPatternPartKind.Operator, Text = "+" }] };
        var result = GrammarEditing.Apply(doc, input, Engine);
        Assert.Equal(0, result.UnmappedManualCount);
        var preserved = result.Document.TextUnits.Single(u => u.Id == example.Id);
        Assert.Equal(example.Tokens, preserved.Tokens); Assert.Equal(example.Segments, preserved.Segments);
        Assert.True(preserved.Tokens[0].Locked); Assert.Equal(extra.Id, result.Document.Grammar!.ExampleUnitIds[0]);
        Assert.Equal(GrammarPatternPartKind.Operator, result.Document.Grammar.PatternParts.Last().Kind);
        Assert.Equal("Another example", result.Document.TextUnits.Single(u => u.Id == extra.Id).Translations["en"]);
        Assert.Equal(doc.Source, result.Document.Source);
    }
    [Fact]
    public void ChangingOneUnitNeverMapsLocksFromAnotherUnit()
    {
        var doc = Grammar(); var input = GrammarEditing.From(doc);
        input = input with { Units = input.Units.Select(u => u.Role == TextUnitRole.Example ? u with { Text = "世界。" } : u with { Text = "你好。" }).ToArray() };
        var result = GrammarEditing.Apply(doc, input, Engine);
        Assert.Equal(1, result.UnmappedManualCount); Assert.DoesNotContain(result.Document.TextUnits.SelectMany(u => u.Tokens), t => t.Locked);
        Assert.Equal(doc.TextUnits.Select(u => u.Id), result.Document.TextUnits.Select(u => u.Id));
    }
    [Fact]
    public void RemovedManualUnitReportsLossAndRemainingUnitIsIntact()
    {
        var doc = Grammar(); var input = GrammarEditing.From(doc);
        input = input with { Units = [input.Units[0], new(Guid.NewGuid(), TextUnitRole.Example, "新例句", new Dictionary<string, string>())] };
        var result = GrammarEditing.Apply(doc, input, Engine); Assert.Equal(1, result.UnmappedManualCount);
        Assert.Equal(doc.TextUnits[0].Tokens, result.Document.TextUnits[0].Tokens);
    }
    [Theory]
    [InlineData("duplicate")][InlineData("role")][InlineData("no-example")][InlineData("translation")][InlineData("blank-part")][InlineData("too-many")]
    public void InvalidStructuresAreRejected(string kind)
    {
        var doc = Grammar(); var input = GrammarEditing.From(doc);
        input = kind switch {
            "duplicate" => input with { Units = [.. input.Units, input.Units[0]] },
            "role" => input with { Units = [input.Units[0] with { Role = TextUnitRole.Example }, input.Units[1]] },
            "no-example" => input with { Units = [input.Units[0]] },
            "translation" => input with { Units = [input.Units[0] with { Translations = new Dictionary<string,string> { ["fr"] = "bad" } }, input.Units[1]] },
            "blank-part" => input with { Parts = [new() { Kind = GrammarPatternPartKind.Literal, Text = " " }] },
            _ => input with { Units = [input.Units[0], .. Enumerable.Range(0,21).Select(_ => new GrammarUnitInput(Guid.NewGuid(), TextUnitRole.Example, "例句", new Dictionary<string,string>()))] }
        };
        Assert.Throws<InvalidDataException>(() => GrammarEditing.Apply(doc, input, Engine));
    }
    [Fact]
    public void InvalidUnicodeAndCancellationDoNotProduceAPreview()
    {
        var doc = Grammar(); var input = GrammarEditing.From(doc);
        Assert.Throws<System.Text.EncoderFallbackException>(() => GrammarEditing.Apply(doc, input with { Title = "\ud800" }, Engine));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => GrammarEditing.Apply(doc, input, Engine, cancel.Token));
    }
}
