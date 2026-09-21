using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Audio;

public sealed class OfflineVoiceInputTests
{
    [Fact]
    public void GouXueLinTouDictionaryDefinitionKeepsEveryWordAndTreatsDiamondAsPause()
    {
        const string definition = "旧时迷信说法，谓狗血淋在妖人头上，就可使其妖法失灵◇形容骂得很凶，使被骂者如淋了狗血的妖人一样，无言以对，无计可施。";
        var hanzi = definition.EnumerateRunes().Where(r => r.Value is >= 0x4e00 and <= 0x9fff).Select(r => r.ToString()).ToHashSet();
        var chunks = OfflineVoiceInput.Chunks(definition);
        OfflineVoiceInput.Validate(definition, hanzi.Contains);
        Assert.Equal(definition.Replace('◇', '，'), string.Concat(chunks.Select(c => OfflineVoiceInput.Prepare(c, hanzi.Contains))));
        Assert.True(chunks.Count > 1);
    }

    private static bool Known(string value) => "你好价格元".Contains(value, StringComparison.Ordinal);
    [Theory]
    [InlineData("你好ABC")]
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
    [InlineData("你好——价格", "你好，价格")]
    [InlineData("你好/价格", "你好，价格")]
    public void DictionaryQuotationAndSeparatorsDoNotRejectOtherwiseSupportedText(string text, string expected)
        => Assert.Equal(expected, OfflineVoiceInput.Prepare(text, Known));

    [Fact]
    public void LongPunctuationRunsDoNotCreateUnplayableNativeChunks()
    {
        var text = "你好" + new string('！', 100) + "你好";
        OfflineVoiceInput.Validate(text, Known);
        Assert.Equal(new[] { "你好！你好" }, OfflineVoiceInput.Chunks(text));
        Assert.All(OfflineVoiceInput.Chunks(text), c => Assert.NotEmpty(OfflineVoiceInput.Prepare(c, Known)));
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

    [Theory]
    [InlineData("\uFEFF你\u200B好\u2060。", "你好。")]
    [InlineData("【你好】\r\n\t价格３．１４元。", "，你好，价格3.14元。")]
    [InlineData("★你好★\n●价格３元", "，你好，价格3元")]
    [InlineData("你\uFE0F好", "你好")]
    [InlineData("①你好\n②价格３元", "1你好，2价格3元")]
    [InlineData("你好👩‍💻👍🏽😀价格", "你好，价格")]
    [InlineData("你好+价格", "你好，价格")]
    [InlineData("价格3%", "价格3，")]
    [InlineData("你好◇◆★→☞✅价格", "你好，价格")]
    [InlineData("你好：价格；你好:价格;", "你好，价格，你好，价格，")]
    public void FormattingIsNormalizedBeforeBothValidationAndPlayback(string input, string expected)
    {
        Assert.Equal(expected, OfflineVoiceInput.Prepare(input, Known));
        Assert.Equal(expected, string.Concat(OfflineVoiceInput.Chunks(input)));
        OfflineVoiceInput.Validate(input, Known);
        Assert.Equal(expected, OfflineVoiceInput.Normalize(expected));
    }

    [Theory]
    [InlineData("★\u200B【】……！！")]
    [InlineData("\uFEFF\n\t")]
    [InlineData("你好𠀀")]
    [InlineData("你好ABC")]
    [InlineData("你好ā")]
    public void CleanupDoesNotSilentlyOmitUnsupportedWordsOrAcceptSymbolOnlyText(string input)
        => Assert.Throws<UnsupportedVoiceTextException>(() => OfflineVoiceInput.Validate(input, Known));

    [Fact]
    public void LaterUnknownWordIsStillCaughtBeforeAnyLongPassagePlayback()
    {
        var input = string.Concat(Enumerable.Repeat("你好。\u200B\n", 100)) + "𠀀";
        Assert.Throws<UnsupportedVoiceTextException>(() => OfflineVoiceInput.Validate(input, Known));
        Assert.All(OfflineVoiceInput.Chunks(input), c => Assert.InRange(c.Length, 1, 48));
    }
}
