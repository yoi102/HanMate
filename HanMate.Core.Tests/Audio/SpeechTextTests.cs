using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Audio;

public sealed class SpeechTextTests
{
    [Fact]
    public void OfflineVoiceChunksBoundNumericExpansionAndRemainLossless()
    {
        var text = "你好，欢迎学习汉语。" + string.Concat(Enumerable.Repeat("12345 ", 20));
        var chunks = SpeechText.Split(text, 48);
        Assert.Equal(text, string.Concat(chunks)); Assert.True(chunks.Count >= 3);
        Assert.All(chunks, c => Assert.InRange(new System.Globalization.StringInfo(c).LengthInTextElements, 1, 48));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeechText.Split(text, 0));
    }
    [Fact]
    public void LongUtteranceUsesLastPhraseBoundaryInsteadOfCuttingFollowingWord()
    {
        var sentence = new string('学', 165) + "。";
        var text = sentence + string.Concat(Enumerable.Repeat("重庆银行欢迎你", 30));
        var chunks = SpeechText.Split(text);
        Assert.Equal(sentence, chunks[0]); Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 180));
    }
    [Fact]
    public void ShortSentencesAndDecimalTextAreNotFragmented()
    {
        const string text = "今天是2026年9月19日。价格3.14元，欢迎学习！";
        Assert.Equal(new[] { text }, SpeechText.Split(text));
    }
    [Fact]
    public void PhraseBoundaryNearEmojiKeepsEveryTextElement()
    {
        var text = string.Concat(Enumerable.Repeat("汉语👩‍👩‍👧‍👦e\u0301，\r\n", 100));
        var chunks = SpeechText.Split(text);
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => { Assert.InRange(c.Length, 1, 500); Assert.False(char.IsLowSurrogate(c[0])); Assert.NotEqual('\u0301', c[0]); Assert.NotEqual('\u200d', c[0]); });
    }
}
