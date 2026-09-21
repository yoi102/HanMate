using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Infrastructure.Dictionary;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class DictionaryDetailTests
{
    [Fact]
    public void LearningMeaningKeepsOnlySavedTextAndReadings()
    {
        var document = Entry(definition: "获取知识。例如：我们一起学习。", examples: ["认真～，天天进步。"]);
        var before = JsonSerializer.Serialize(document, ContentJson.Options);
        var detail = DictionaryDetailProjection.Create(document, sourceOnly: true);
        Assert.Equal(document.TextUnits.Where(u => u.Role == TextUnitRole.Definition).Select(u => u.Text), detail.Definitions.Select(d => d.Text));
        Assert.Equal("认真～，天天进步。", Assert.Single(detail.Examples).Text);
        Assert.False(detail.Examples[0].Supplement); Assert.False(detail.HasAutomaticPinyin);
        Assert.All(detail.Definitions[0].Atoms, a => Assert.Null(a.Pinyin));
        Assert.Equal(new[] { "xué", "xí" }, detail.Headword.Atoms.Select(a => a.Pinyin));
        Assert.Equal(before, JsonSerializer.Serialize(document, ContentJson.Options));
    }

    [Fact]
    public void LearningMeaningDoesNotBorrowDictionaryExamplesAndObservesCancellation()
    {
        var document = Entry();
        Assert.NotEmpty(DictionaryDetailProjection.Create(document).Examples);
        Assert.Empty(DictionaryDetailProjection.Create(document, sourceOnly: true).Examples);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DictionaryDetailProjection.Create(document, stop.Token, sourceOnly: true));
    }

    [Fact]
    public void PassageSpeechKeepsWholeExampleAndExpandsPlaceholderOnlyInView()
    {
        var document = Entry(examples: ["认真～，天天进步。"]);
        var detail = DictionaryDetailProjection.Create(document);
        var target = HanMate.Core.Reading.DictionarySpeechTarget.Create(document, detail.Examples[0].Atoms);
        Assert.Equal("认真学习，天天进步。", target.Text);
        Assert.Null(target.SourceUnitId); Assert.False(target.WholeUnit);
        Assert.Equal("认真～，天天进步。", document.TextUnits.Single(u => u.Role == TextUnitRole.Example).Text);
    }
    [Fact]
    public void DefinitionParagraphSpeechDoesNotSelectWholeUnitRecordingOrAnotherParagraph()
    {
        var document = Entry(definition: " 第一段解释。\n第二段解释。 ");
        var atoms = DictionaryDetailProjection.Create(document).Definitions[0].Atoms;
        var paragraphs = HanMate.Core.Reading.DictionaryPresentation.DefinitionParagraphs(atoms);
        var first = HanMate.Core.Reading.DictionarySpeechTarget.Create(document, paragraphs[0]);
        var second = HanMate.Core.Reading.DictionarySpeechTarget.Create(document, paragraphs[1]);
        Assert.Equal("第一段解释。", first.Text); Assert.Equal("第二段解释。", second.Text);
        Assert.NotEqual(first.Identity, second.Identity); Assert.Equal(first.SourceUnitId, second.SourceUnitId);
        Assert.NotNull(first.SourceUnitId); Assert.False(first.WholeUnit); Assert.False(second.WholeUnit);
    }
    [Fact]
    public void LongSpeechTargetIsIndependentOfVisualPageLimitAndKeepsUnicode()
    {
        var text = string.Concat(Enumerable.Repeat("学习知识𠀀。",40));
        var document = Entry(definition:text);
        var atoms = DictionaryDetailProjection.Create(document).Definitions[0].Atoms;
        Assert.True(atoms.Count > HanMate.Core.Reading.ReadingDocument.PageAtomLimit);
        var target = HanMate.Core.Reading.DictionarySpeechTarget.Create(document,atoms);
        Assert.Equal(text,target.Text); Assert.True(target.WholeUnit);
        Assert.Equal(document.TextUnits.First(u=>u.Role==TextUnitRole.Definition).Id,target.SourceUnitId);
    }
    private static ContentDocument Entry(string word = "学习", string definition = "求取知识，掌握技能。", string[]? examples = null) =>
        DefaultDictionaryStore.Project(Guid.NewGuid(), new(word, word == "学习" ? "xué xí" : "", false, [definition], examples ?? [], ["出处：测试原文"]));

    [Theory]
    [InlineData("银行", "yín háng")]
    [InlineData("行长", "háng zhǎng")]
    [InlineData("长大", "zhǎng dà")]
    [InlineData("音乐", "yīn yuè")]
    [InlineData("快乐", "kuài lè")]
    [InlineData("重庆", "chóng qìng")]
    [InlineData("轻轻地", "qīng qīng de")]
    [InlineData("认真地", "rèn zhēn de")]
    [InlineData("玻璃", "bō li")]
    public void OfflineDisplayAnnotationUsesPhraseContext(string text, string expected)
        => Assert.Equal(expected, string.Join(" ", DictionaryDisplayPinyin.Annotate(text).OrderBy(p => p.Key).Select(p => p.Value)));

    [Fact]
    public void DefinitionsGetReadingsWithoutMutatingOriginalOrManualHeadword()
    {
        var document = Entry();
        var json = JsonSerializer.Serialize(document, ContentJson.Options);
        var detail = DictionaryDetailProjection.Create(document);
        Assert.True(detail.HasAutomaticPinyin);
        Assert.All(detail.Definitions[0].Atoms.Where(a => a.Text != "，" && a.Text != "。"), a => Assert.NotNull(a.Pinyin));
        Assert.Equal(new[] { "xué", "xí" }, detail.Headword.Atoms.Select(a => a.Pinyin));
        Assert.Equal(json, JsonSerializer.Serialize(document, ContentJson.Options));
        Assert.Contains("出处：测试原文", detail.Notes);
        Assert.DoesNotContain(detail.Definitions, d => d.Text.Contains("出处"));
    }

    [Fact]
    public void SourceExamplesAndExplicitUsagesAreKeptAndPlaceholderExpandsOnlyInView()
    {
        var document = Entry("学习", "获取知识。例如：我们一起学习。", ["认真～，天天进步。"]);
        var detail = DictionaryDetailProjection.Create(document);
        Assert.Equal("认真学习，天天进步。", detail.Examples[0].Text);
        Assert.False(detail.Examples[0].Supplement);
        Assert.Contains(detail.Examples, e => e.Text == "我们一起学习。");
        Assert.Equal("认真～，天天进步。", document.TextUnits.Single(u => u.Role == TextUnitRole.Example).Text);
    }

    [Fact]
    public void SupplementalExamplesAreLabeledAndUnknownWordsAreNotInvented()
    {
        var detail = DictionaryDetailProjection.Create(Entry());
        Assert.Contains(detail.Examples, e => e.Supplement && e.Text.Contains("学习"));
        Assert.Empty(DictionaryDetailProjection.Create(Entry("测试不存在的词" )).Examples);
    }

    [Theory]
    [InlineData("学习，然后继续学习。", "学习", "0,1,7,8")]
    [InlineData("𠀀学习🙂学习", "学习", "1,2,4,5")]
    [InlineData("很好，非常好。", "好", "1,5")]
    [InlineData("你好", "学习", "")]
    public void HighlightUsesTextElementsAndMatchesTheWholeTerm(string text, string term, string expected)
        => Assert.Equal(expected, string.Join(",", DictionaryDetailProjection.HighlightStarts(text, term).Order()));

    [Fact]
    public void LongDefinitionStaysCompleteAndCancellationIsObserved()
    {
        var text = string.Concat(Enumerable.Repeat("学习知识。\n", 300));
        var detail = DictionaryDetailProjection.Create(Entry(definition: text));
        Assert.Equal(text, string.Concat(detail.Definitions[0].Atoms.Select(a => a.Text)));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DictionaryDetailProjection.Create(Entry(), stop.Token));
    }

    [Fact]
    public void VisualCompactionRemovesBlankLinesWithoutChangingSourceOrHighlightOffsets()
    {
        var document = Entry("学习", "  学习\n \n\n 知识。  ");
        var original = DictionaryDetailProjection.Create(document).Definitions[0];
        var compact = HanMate.Core.Reading.DictionaryPresentation.Compact(original.Atoms);
        Assert.Equal("学习\n知识。", string.Concat(compact.Select(a => a.Text)));
        Assert.Equal("  学习\n \n\n 知识。  ", original.Text);
        Assert.Equal(new[] { 2, 3 }, compact.Take(2).Select(a => a.Start));
    }

    [Fact]
    public void DefinitionParagraphsHideOnlyEmptyMarkersAndPreserveUnicodeReadingsAndOffsets()
    {
        const string source = "\n(1)\n 学习知识。\n\n（２）\n②\n2.\n3、\n⒌\n⑺\n𠀀学习🙂\n2026\n①有内容\n";
        var original = DictionaryDetailProjection.Create(Entry(definition: source)).Definitions[0];
        var paragraphs = HanMate.Core.Reading.DictionaryPresentation.DefinitionParagraphs(original.Atoms);
        Assert.Equal(new[] { "学习知识。", "𠀀学习🙂", "2026", "①有内容" },
            paragraphs.Select(p => string.Concat(p.Select(a => a.Text))));
        Assert.All(paragraphs.SelectMany(p => p), atom => Assert.Contains(atom, original.Atoms));
        Assert.Equal(source, original.Text);
        Assert.Equal(source, string.Concat(original.Atoms.Select(a => a.Text)));
    }

    [Fact]
    public void LatinAndUnmappedCharactersDoNotReceiveGuessedPinyin()
    {
        var values = DictionaryDisplayPinyin.Annotate("ABC🙂\U000323AF学习");
        Assert.DoesNotContain(values.Keys, i => i < 5);
        Assert.Equal("xué", values[5]); Assert.Equal("xí", values[6]);
    }
}
