using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Audio;

public sealed class MeloPronunciationLexiconTests
{
    [Theory]
    [InlineData("你好", "_你好_")]
    [InlineData("你好。", "_你好。")]
    [InlineData("，你好！", "，你好！")]
    public void SentencePunctuationDoesNotCreateAnExtraBlankOnlyNativeUtterance(string text, string expected)
        => Assert.Equal(expected, MeloPronunciationLexicon.WithBoundaries(text));

    [Theory]
    [InlineData("n i3 #0", "n i", "3 3")]
    [InlineData("n i3 #0 h ao3 #0", "n i h ao", "3 3 3 3")]
    [InlineData("m a1 #0 m a5 #0", "m a m a", "1 1 5 5")]
    [InlineData("^ in2 #0 h ang2 #0", "y in h ang", "2 2 2 2")]
    [InlineData("^ ong1 #0", "ong", "1")]
    public void NativeLexiconPreservesEveryPhoneAndToneWithoutAddingASpokenCarrier(string input, string phones, string tones)
    {
        var lexicon = new MeloPronunciationLexicon(phones.Split(' '));
        var rows = lexicon.Entries.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(row => row.Split(' ')).ToDictionary(row => row[0][0]);
        var encoded = lexicon.Encode(input);
        Assert.All(encoded, alias => Assert.InRange((int)alias, 0xe000, 0xf8ff));
        Assert.Equal(phones, string.Join(' ', encoded.Select(alias => rows[alias][1])));
        Assert.Equal(tones, string.Join(' ', encoded.Select(alias => rows[alias][2])));
        Assert.Equal("_" + encoded + "_", MeloPronunciationLexicon.WithBoundaries(encoded));
    }

    [Fact]
    public void OtherRequestsAndTokenOrderingDoNotChangePronunciationAliases()
    {
        var lexicon = new MeloPronunciationLexicon(["n", "i", "h", "ao"]);
        var first = lexicon.Encode("n i3 #0");
        _ = lexicon.Encode("h ao3 #0");
        Assert.Equal(first, lexicon.Encode("n i3 #0"));
        var reordered = new MeloPronunciationLexicon(["ao", "h", "i", "n", "i"]);
        Assert.Equal(lexicon.Entries, reordered.Entries);
        Assert.NotEqual(first, lexicon.Encode("n i2 #0"));
    }

    [Fact]
    public void UnknownPhoneIsRejectedBeforeNativeEngineCanOmitIt()
    {
        var lexicon = new MeloPronunciationLexicon(["n", "i"]);
        Assert.Throws<InvalidDataException>(() => lexicon.Encode("h ao3 #0"));
        Assert.Throws<InvalidDataException>(() => lexicon.Encode("n i0 #0"));
    }
}
