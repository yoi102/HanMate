using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Audio;

public sealed class OfflineVoiceInputTests
{
    private static bool Known(string value) => "你好价格元".Contains(value, StringComparison.Ordinal);
    [Theory]
    [InlineData("你好ABC")]
    [InlineData("你好👩‍💻")]
    [InlineData("你𠀀好")]
    [InlineData("你好\0")]
    [InlineData("！？")]
    public void UnsupportedTextIsRejectedWithoutEmbeddingUserDataInError(string text)
    {
        var error = Assert.Throws<UnsupportedVoiceTextException>(() => OfflineVoiceInput.Prepare(text, Known));
        Assert.DoesNotContain(text, error.Message);
    }
    [Fact]
    public void ChineseAndNumbersKeepReadingsAndOnlyPausesAreNormalized()
    {
        Assert.Equal("你好，价格3.14元。", OfflineVoiceInput.Prepare("你好\n价格３.１４元。", Known));
        Assert.Equal("，你好，", OfflineVoiceInput.Prepare("（你好）", Known));
    }

    [Theory]
    [InlineData("\"你好\"", "，你好，")]
    [InlineData("你好——价格", "你好，，价格")]
    [InlineData("你好/价格", "你好，价格")]
    public void DictionaryQuotationAndSeparatorsDoNotRejectOtherwiseSupportedText(string text, string expected)
        => Assert.Equal(expected, OfflineVoiceInput.Prepare(text, Known));

    [Fact]
    public void PreflightRejectsPunctuationOnlyNativeChunkBeforeAnyPlayback()
    {
        var text = "你好" + new string('！', 100) + "你好";
        // A single 180-element preflight used to pass, then fail after the first 48-element playback chunk.
        Assert.NotEmpty(OfflineVoiceInput.Prepare(text, Known));
        Assert.Throws<UnsupportedVoiceTextException>(() => OfflineVoiceInput.Validate(text, Known));
    }

    [Fact]
    public void BlankNativeChunksAreSkippedConsistentlyAndLaterUnknownInputIsRejected()
    {
        var text = "你好" + new string('\n', 100) + "价格３.１４元。";
        OfflineVoiceInput.Validate(text, Known);
        var chunks = OfflineVoiceInput.Chunks(text);
        Assert.All(chunks, c => { Assert.False(string.IsNullOrWhiteSpace(c)); Assert.InRange(c.Length, 1, 48); });
        Assert.Throws<UnsupportedVoiceTextException>(() => OfflineVoiceInput.Validate(text + "ABC", Known));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => OfflineVoiceInput.Validate(text, Known, cancel.Token));
    }
}
