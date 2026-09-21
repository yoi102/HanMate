using HanMate.Core.Content;

namespace HanMate.Core.Tests.Content;

public sealed class PinyinFormatterTests
{
    [Theory]
    [InlineData("ni", 3, false, "nǐ")]
    [InlineData("liu", 2, false, "liú")]
    [InlineData("gui", 4, false, "guì")]
    [InlineData("lv", 4, false, "lǜ")]
    [InlineData("ma", 0, false, "ma")]
    [InlineData("hua", 1, true, "huār")]
    public void Format_AppliesTeachingToneRules(string syllable, int tone, bool erhua, string expected)
    {
        Assert.Equal(expected, PinyinFormatter.Format(syllable, tone, erhua));
    }
}
