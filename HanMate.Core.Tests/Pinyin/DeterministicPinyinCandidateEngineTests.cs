using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Tests.Pinyin;

public sealed class DeterministicPinyinCandidateEngineTests
{
    [Fact]
    public void Annotate_UsesLongestPhrasesThenCharacterFallbackAndPreservesNonHanzi()
    {
        var engine = new DeterministicPinyinCandidateEngine(
        [
            Phrase("银行", "yin2 hang2"),
            Phrase("行走", "xing2 zou3"),
            Character("我", "wo3"),
            Character("去", "qu4"),
            Character("行", "xing2")
        ]);

        var result = engine.Annotate("我去银行行走🙂");

        Assert.Equal(["我", "去", "银行", "行走", "🙂"], result.Select(item => item.Text));
        Assert.Equal(PinyinDecisionReason.LongestPhrase, result[2].Reason);
        Assert.Equal(["yín", "háng"], result[2].Selected!.Syllables.Select(item => item.Display));
        Assert.Equal(PinyinDecisionReason.LongestPhrase, result[3].Reason);
        Assert.Equal(AnnotationReviewState.NeedsReview, result[3].ReviewState);
        Assert.Equal(PinyinDecisionReason.NonHanzi, result[4].Reason);
    }

    [Fact]
    public void Annotate_ReviewedPhraseWinsBeforeLongerDictionaryPhrase()
    {
        var engine = new DeterministicPinyinCandidateEngine(
        [
            Phrase("银行", "yin2 hang2", PinyinLexiconTier.ReviewedPhrase, "reviewed"),
            Phrase("银行家", "yin2 hang2 jia1"),
            Character("家", "jia1")
        ]);

        var result = engine.Annotate("银行家");

        Assert.Equal(["银行", "家"], result.Select(item => item.Text));
        Assert.Equal(PinyinDecisionReason.ReviewedPhrase, result[0].Reason);
        Assert.Equal(AnnotationReviewState.Confirmed, result[0].ReviewState);
    }

    [Fact]
    public void Annotate_KeepsSameRankReadingsAsUnselectedCandidates()
    {
        var engine = new DeterministicPinyinCandidateEngine(
        [
            Phrase("朝阳", "zhao1 yang2", sourceId: "phrase-pinyin-data"),
            Phrase("朝阳", "chao2 yang2", sourceId: "phrase-pinyin-data")
        ]);

        var result = Assert.Single(engine.Annotate("朝阳"));

        Assert.Equal(PinyinDecisionReason.AmbiguousAtBestRank, result.Reason);
        Assert.Equal(AnnotationReviewState.NeedsReview, result.ReviewState);
        Assert.Null(result.Selected);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(["cháo", "zhāo"], result.Candidates.Select(item => item.Syllables[0].Display));
    }

    [Theory]
    [InlineData("银行", "yin2 hang2", "yín háng")]
    [InlineData("行走", "xing2 zou3", "xíng zǒu")]
    [InlineData("音乐", "yin1 yue4", "yīn yuè")]
    [InlineData("快乐", "kuai4 le4", "kuài lè")]
    [InlineData("重新", "chong2 xin1", "chóng xīn")]
    [InlineData("重量", "zhong4 liang4", "zhòng liàng")]
    [InlineData("长大", "zhang3 da4", "zhǎng dà")]
    [InlineData("长度", "chang2 du4", "cháng dù")]
    public void Annotate_ResolvesReviewedPolyphonicTrialCases(string text, string reading, string expected)
    {
        var engine = new DeterministicPinyinCandidateEngine(
            [Phrase(text, reading, PinyinLexiconTier.ReviewedPhrase, "reviewed-trial")]);

        var result = Assert.Single(engine.Annotate(text));

        Assert.Equal(expected, string.Join(' ', result.Selected!.Syllables.Select(item => item.Display)));
        Assert.Equal(AnnotationReviewState.Confirmed, result.ReviewState);
    }

    [Fact]
    public void Annotate_LockedRangeCannotBeCrossedByPhraseMatch()
    {
        var engine = new DeterministicPinyinCandidateEngine(
        [
            Phrase("银行", "yin2 hang2"),
            Character("银", "yin2")
        ]);
        var locked = new LockedPinyinRange
        {
            Start = 1,
            Length = 1,
            Syllables = [Syllable("xing2")]
        };

        var result = engine.Annotate("银行", [locked]);

        Assert.Equal(["银", "行"], result.Select(item => item.Text));
        Assert.Equal(PinyinDecisionReason.CharacterFallback, result[0].Reason);
        Assert.Equal(PinyinDecisionReason.LockedManual, result[1].Reason);
        Assert.Equal("xíng", result[1].Selected!.Syllables[0].Display);
    }

    [Fact]
    public void Annotate_ReportsUnknownWithoutInventingNeutralTone()
    {
        var result = Assert.Single(new DeterministicPinyinCandidateEngine([]).Annotate("龘"));

        Assert.Equal(PinyinDecisionReason.Unknown, result.Reason);
        Assert.Equal(AnnotationReviewState.Unknown, result.ReviewState);
        Assert.Null(result.Selected);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Constructor_RejectsPhraseWhoseSyllableCountDoesNotMatchTextElements()
    {
        var invalid = Phrase("银行", "yin2");

        Assert.Throws<ArgumentException>(() => new DeterministicPinyinCandidateEngine([invalid]));
    }

    private static PinyinLexiconEntry Phrase(
        string text,
        string reading,
        PinyinLexiconTier tier = PinyinLexiconTier.PhraseDictionary,
        string sourceId = "fixture",
        int priority = 0) => new()
        {
            Text = text,
            Syllables = reading.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Syllable).ToArray(),
            Tier = tier,
            Priority = priority,
            SourceId = sourceId
        };

    private static PinyinLexiconEntry Character(string text, string reading) =>
        Phrase(text, reading, PinyinLexiconTier.CharacterDictionary);

    private static PinyinSyllable Syllable(string value) => PinyinSyllableParser.ParseDictionarySyllable(value);
}
