using HanMate.Core.Audio;
using HanMate.Core.Pinyin;
using HanMate.Core.Content;

namespace HanMate.Core.Tests.Audio;

public sealed class PinyinVoiceInputTests
{
    [Theory]
    [InlineData("bo", 1, "b o1 #0")]
    [InlineData("fo", 1, "f o1 #0")]
    [InlineData("le", 1, "l e1 #0")]
    [InlineData("lü", 4, "l v4 #0")]
    [InlineData("ju", 3, "j v3 #0")]
    [InlineData("qun", 2, "q vn2 #0")]
    [InlineData("xue", 2, "x ve2 #0")]
    [InlineData("yuan", 3, "^ van3 #0")]
    [InlineData("yun", 2, "^ vn2 #0")]
    [InlineData("zi", 3, "z ii3 #0")]
    [InlineData("shi", 4, "sh iii4 #0")]
    [InlineData("ri", 4, "r iii4 #0")]
    [InlineData("liu", 4, "l iou4 #0")]
    [InlineData("gui", 1, "g uei1 #0")]
    [InlineData("lun", 2, "l uen2 #0")]
    [InlineData("you", 3, "^ iou3 #0")]
    [InlineData("wen", 2, "^ uen2 #0")]
    [InlineData("ma", 0, "m a5 #0")]
    [InlineData("ong", 1, "^ ong1 #0")]
    public void MapsAnnotatedReadingsToModelPhonemes(string spelling, int tone, string expected)
        => Assert.Equal(expected, PinyinVoiceInput.Syllable(spelling, tone));

    [Theory]
    [InlineData("")]
    [InlineData("bo1")]
    [InlineData("bō")]
    [InlineData("b")]
    [InlineData("BO")]
    [InlineData("hello")]
    [InlineData("v n")]
    public void RejectsUnannotatedOrUnsupportedInput(string spelling)
        => Assert.Throws<InvalidDataException>(() => PinyinVoiceInput.Syllable(spelling, 1));

    [Fact]
    public void EveryTeachingButtonAndExampleCanUseExplicitPhonemes()
    {
        var course = PinyinCourse.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-course.json")));
        foreach (var item in course.Data.Items)
        {
            Assert.Equal(3, PinyinVoiceInput.Demo(item).Split(' ').Length);
            foreach (var example in item.Examples)
                Assert.Equal(course.Unit(example).Tokens.Count * 3, PinyinVoiceInput.Example(course.Unit(example)).Split(' ').Length);
        }
        var mother = course.Data.Contents.Single(d => d.Title == "妈妈").TextUnits[0];
        Assert.Equal("m a1 #0 m a5 #0", PinyinVoiceInput.Example(mother));
        Assert.Throws<InvalidDataException>(() => PinyinVoiceInput.Example(mother with {
            Tokens = [mother.Tokens[0] with { Pinyin = null }] }));
        Assert.Throws<InvalidDataException>(() => PinyinVoiceInput.Example(mother with {
            Tokens = [mother.Tokens[0] with { Pinyin = mother.Tokens[0].Pinyin! with { Erhua = true } }] }));
    }
}
