using System.Text;
using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Audio;

public sealed class CompressedAudioTests
{
    private static byte[] Fixture(string extension) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio", "tone." + extension));
    [Theory][InlineData("mp3")][InlineData("m4a")]
    public void RealEncodedFixturesHaveExpectedFormatAndResetStream(string extension)
    { using var input = new MemoryStream(Fixture(extension)); Assert.Equal(new(extension, 44100, 1), CompressedAudio.Inspect(input)); Assert.Equal(0, input.Position); }
    [Theory][InlineData("mp3")][InlineData("m4a")]
    public void TruncatedCompressedFramesAndContainersAreRejected(string extension)
    {
        var bytes = Fixture(extension);
        foreach (var cut in new[] { 1, 9, 64, bytes.Length / 2 })
        { using var input = new MemoryStream(bytes[..^cut]); Assert.Throws<InvalidDataException>(() => CompressedAudio.Inspect(input)); }
    }
    [Fact]
    public void AacProfileAndExternalDataReferenceAreRejectedBeforeNativeDecoding()
    {
        var original = Fixture("m4a");
        var config = original.AsSpan().IndexOf(new byte[] { 0x12, 0x08, 0x56, 0xe5, 0 }); Assert.True(config > 0);
        var he = original.ToArray(); he[config] = (byte)((5 << 3) | (he[config] & 7));
        using (var input = new MemoryStream(he)) Assert.Throws<InvalidDataException>(() => CompressedAudio.Inspect(input));
        var external = original.ToArray(); var url = external.AsSpan().IndexOf("url "u8); Assert.True(url > 0); external[url + 7] = 0;
        using (var input = new MemoryStream(external)) Assert.Throws<InvalidDataException>(() => CompressedAudio.Inspect(input));
    }
    [Fact]
    public void ForgedMp3HeadersAndId3LengthsDoNotBecomeAudio()
    {
        var bytes = Fixture("mp3"); Assert.Equal("ID3", Encoding.ASCII.GetString(bytes, 0, 3)); bytes[6] = 0x7f;
        using (var input = new MemoryStream(bytes)) Assert.Throws<InvalidDataException>(() => CompressedAudio.Inspect(input));
        using var fake = new MemoryStream("not an audio file, even if its name is .mp3"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => CompressedAudio.Inspect(fake));
    }
    [Fact]
    public void SpeechChunksPreserveAllUnicodeAndRejectUnboundedTextElements()
    {
        var text = string.Concat(Enumerable.Repeat("你好𠀀e\u0301👩‍👩‍👧\r\n", 130));
        var chunks = SpeechText.Split(text); Assert.True(chunks.Count > 1); Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => { Assert.InRange(c.Length, 1, 500); Assert.False(char.IsLowSurrogate(c[0])); Assert.NotEqual('\u0301', c[0]); });
        Assert.Throws<InvalidDataException>(() => SpeechText.Split("a" + new string('\u0301', 501)));
    }
}
