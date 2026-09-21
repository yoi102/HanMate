using HanMate.Core.Audio;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Tests.Audio;

public sealed class PinyinRecordingPolicyTests
{
    [Theory]
    [InlineData("趴", "pa1")]
    [InlineData("怕", "pa4")]
    [InlineData("吃", "chi1")]
    [InlineData("赤", "chi4")]
    public void ReportedErrorsAlwaysSelectMatchingRecording(string text, string key)
    {
        var course = PinyinCourse.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-course.json")));
        var examples = course.Data.Items.SelectMany(i => i.Examples).Where(e => course.Unit(e).Text == text).ToArray();
        Assert.NotEmpty(examples);
        Assert.All(examples, e => {
            Assert.Equal(key, course.PlaybackKey(e));
            Assert.True(PinyinRecordingPolicy.PreferRecording(PinyinVoiceInput.Example(course.Unit(e)), course.PlaybackKey(e), preferAi: true));
        });
    }

    [Theory]
    [InlineData("h ai4 #0 p a4 #0", "word-hai-pa", true)]
    [InlineData("ch iii1 #0 f an4 #0", "word-chi-fan", true)]
    [InlineData("p a1 #0", null, false)]
    [InlineData("p ai4 #0", "pai4", false)]
    [InlineData("p a2 #0", "pa2", false)]
    [InlineData("zh iii1 #0", "zhi1", false)]
    [InlineData("b a1 #0", "ba1", false)]
    [InlineData("b a1 #0", "local:recording", true)]
    public void LimitsFallbackToAffectedReadingsAndPreservesLocalAudio(string phones, string? recording, bool expected)
        => Assert.Equal(expected, PinyinRecordingPolicy.PreferRecording(phones, recording, preferAi: true));

    [Fact]
    public void DefaultPreservesEveryExistingCourseRecording()
    {
        var course = PinyinCourse.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-course.json")));
        foreach (var item in course.Data.Items)
        {
            var demo = course.DemoPlaybackKey(item);
            if (demo is not null) Assert.True(PinyinRecordingPolicy.PreferRecording("", demo));
            foreach (var example in item.Examples)
            {
                var key = course.PlaybackKey(example);
                if (key is not null) Assert.True(PinyinRecordingPolicy.PreferRecording(PinyinVoiceInput.Example(course.Unit(example)), key));
            }
        }
    }

    [Fact]
    public void MissingRecordingCanUseVoiceWithoutEnablingAiFirst()
        => Assert.False(PinyinRecordingPolicy.PreferRecording("b a1 #0", null));
}
