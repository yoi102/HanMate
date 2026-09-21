using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Pinyin;

public sealed class LessonHeadingAnnotationTests
{
    [Theory]
    [InlineData("自我介绍")]
    [InlineData("我的家")]
    [InlineData("一天的生活")]
    [InlineData("在学校")]
    [InlineData("去买东西")]
    [InlineData("问路")]
    [InlineData("静夜思")]
    [InlineData("春晓")]
    [InlineData("咏鹅")]
    [InlineData("悯农（其二）")]
    [InlineData("登鹳雀楼")]
    [InlineData("江雪")]
    [InlineData("一起学习")]
    [InlineData("读书小句（原创测试）")]
    [InlineData("唐 · 李白")]
    [InlineData("唐 · 孟浩然")]
    [InlineData("唐 · 骆宾王")]
    [InlineData("唐 · 李绅")]
    [InlineData("唐 · 王之涣")]
    [InlineData("唐 · 柳宗元")]
    public void BundledHeadingsPreserveTextAndHaveCompleteDisplayAndSpeechReadings(string text)
    {
        var heading = LessonHeadingAnnotation.Create(text);
        Assert.Equal(text, string.Concat(heading.Atoms.Select(a => a.Text)));
        Assert.DoesNotContain(heading.Atoms, a => a.MissingPinyin);
        Assert.NotNull(heading.Phonemes);
        Assert.Equal(heading.Atoms.Count(a => a.Pinyin is not null), heading.Phonemes!.Split("#0").Length - 1);
    }

    [Fact]
    public void AuthorAndTitlePolyphonesUseTheirOwnReadings()
    {
        Assert.Equal("mèng", LessonHeadingAnnotation.Create("唐 · 孟浩然").Atoms.Single(a => a.Text == "孟").Pinyin);
        Assert.Equal("guàn", LessonHeadingAnnotation.Create("登鹳雀楼").Atoms.Single(a => a.Text == "鹳").Pinyin);
        Assert.Equal("de", LessonHeadingAnnotation.Create("我的家").Atoms.Single(a => a.Text == "的").Pinyin);
    }

    [Fact]
    public void CustomHeadingsKeepUnsupportedTextWithoutInventingPhonemes()
    {
        var heading = LessonHeadingAnnotation.Create("ABC🌷");
        Assert.Equal("ABC🌷", string.Concat(heading.Atoms.Select(a => a.Text)));
        Assert.Null(heading.Phonemes);
        Assert.All(heading.Atoms, a => Assert.Null(a.Pinyin));
    }
}
