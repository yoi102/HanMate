using HanMate.Core.Search;

namespace HanMate.Core.Tests;

public sealed class SearchQueryTests
{
    [Theory]
    [InlineData("nihao")]
    [InlineData(" NIHAO ")]
    [InlineData("ni hao")]
    [InlineData("ni'hao")]
    [InlineData("nǐ hǎo")]
    [InlineData("ni3hao3")]
    [InlineData("ni3 hao3")]
    [InlineData("nǐhǎo")]
    [InlineData("nǐhǎo")]
    [InlineData("ni3hao")]
    [InlineData("ni’hao")]
    public void ConfirmedSyllablesResolveJoinedAndPartialToneQueries(string input) =>
        Assert.Equal(SearchMatchTier.PinyinExact, SearchQuery.Parse(input).Match("你好", "ni hao", "ni3 hao3"));

    [Theory]
    [InlineData("nv")]
    [InlineData("nü")]
    [InlineData("nu:")]
    [InlineData("nǚ")]
    [InlineData("nǚ")]
    [InlineData("nv3")]
    public void UmlautIsPreserved(string input) =>
        Assert.Equal(SearchMatchTier.PinyinExact, SearchQuery.Parse(input).Match("女", "nv", "nv3"));

    [Theory]
    [InlineData("nu", "nv", "nv3")]
    [InlineData("xi'an", "xian", "xian1")]
    [InlineData("xi an", "xian", "xian1")]
    [InlineData("ni2hao3", "ni hao", "ni3 hao3")]
    [InlineData("ni3haò", "ni hao", "ni3 hao3")]
    [InlineData("n3ihao", "ni hao", "ni3 hao3")]
    [InlineData("nǐ4hao", "ni hao", "ni3 hao3")]
    public void ExplicitConstraintsCannotBecomeExactOrApproximateSilently(string query, string bases, string tones) =>
        Assert.Null(SearchQuery.Parse(query).Match("词", bases, tones));

    [Theory]
    [InlineData("xian", "xi an", "xi1 an1")]
    [InlineData("xian", "xian", "xian1")]
    [InlineData("xi'an", "xi an", "xi1 an1")]
    [InlineData("ma5", "ma", "ma0")]
    [InlineData("ma0", "ma", "ma0")]
    [InlineData("yin2hang2", "yin hang", "yin2 hang2")]
    public void BoundariesNeutralAndPolyphonesUseCandidatePronunciation(string query, string bases, string tones) =>
        Assert.Equal(SearchMatchTier.PinyinExact, SearchQuery.Parse(query).Match("词", bases, tones));

    [Theory]
    [InlineData("")]
    [InlineData("%_")]
    [InlineData("\\")]
    [InlineData("3ni")]
    [InlineData("ni33")]
    [InlineData("hello!")]
    [InlineData("😀")]
    public void InvalidInputIsEmpty(string query) => Assert.False(SearchQuery.Parse(query).IsValid);

    [Fact]
    public void ExactPrefixAndContainsHaveDistinctTiers()
    {
        Assert.Equal(SearchMatchTier.HanziExact, SearchQuery.Parse("你好").Match("你好", "", ""));
        Assert.Equal(SearchMatchTier.Prefix, SearchQuery.Parse("你").Match("你好", "", ""));
        Assert.Equal(SearchMatchTier.Contains, SearchQuery.Parse("好").Match("你好", "", ""));
        Assert.Equal(SearchMatchTier.Prefix, SearchQuery.Parse("ni").Match("你好", "ni hao", "ni3 hao3"));
        Assert.Equal(SearchMatchTier.Contains, SearchQuery.Parse("hao3").Match("你好", "ni hao", "ni3 hao3"));
    }
}
