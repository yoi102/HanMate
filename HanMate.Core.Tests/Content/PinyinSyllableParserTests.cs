using System.Text.Json;
using HanMate.Core.Content;

namespace HanMate.Core.Tests.Content;

public sealed class PinyinSyllableParserTests
{
    [Fact]
    public void ParseDictionarySyllable_MatchesPlanningCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-cases.json")));

        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var result = PinyinSyllableParser.ParseDictionarySyllable(item.GetProperty("input").GetString()!);

            Assert.Equal(item.GetProperty("base").GetString(), result.Base);
            Assert.Equal(item.GetProperty("tone").GetInt32(), result.Tone);
            Assert.Equal(item.GetProperty("display").GetString(), result.Display);
        }
    }

    [Theory]
    [InlineData("huar1", "hua", 1, true, "huār")]
    [InlineData("ma5", "ma", 0, false, "ma")]
    [InlineData("lu:4", "lü", 4, false, "lǜ")]
    public void ParseDictionarySyllable_HandlesAliasesAndErhua(
        string input,
        string expectedBase,
        int expectedTone,
        bool expectedErhua,
        string expectedDisplay)
    {
        var result = PinyinSyllableParser.ParseDictionarySyllable(input);

        Assert.Equal(expectedBase, result.Base);
        Assert.Equal(expectedTone, result.Tone);
        Assert.Equal(expectedErhua, result.Erhua);
        Assert.Equal(expectedDisplay, result.Display);
    }

    [Theory]
    [InlineData("ni6")]
    [InlineData("ni33")]
    [InlineData("nǐ3")]
    [InlineData("ni 3")]
    public void TryParseDictionarySyllable_RejectsInvalidToneForms(string input)
    {
        Assert.False(PinyinSyllableParser.TryParseDictionarySyllable(input, out _));
    }
}
