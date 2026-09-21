using System.Text;
using HanMate.Core.Content;

namespace HanMate.Core.Tests.Content;

public sealed class TextDraftInputTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("le")]
    [InlineData("be")]
    public async Task StrictDecodingPreservesTextElementsAndLineEndings(string kind)
    {
        var text = "繁體\r\n\r\ne\u0301 😀\n";
        Encoding encoding = kind switch { "le" => new UnicodeEncoding(false,true,true), "be" => new UnicodeEncoding(true,true,true), "utf8bom" => new UTF8Encoding(true,true), _ => new UTF8Encoding(false,true) };
        using var stream = new MemoryStream(encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
        Assert.Equal(text, await TextDraftInput.ReadAsync(stream));
    }
    [Theory]
    [InlineData(new byte[] { 0xC0, 0xAF })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00 })]
    [InlineData(new byte[] { 0xFE, 0xFF, 0xD8, 0x00 })]
    public async Task InvalidEncodingIsRejected(byte[] bytes)
    { using var stream = new MemoryStream(bytes); await Assert.ThrowsAsync<DecoderFallbackException>(() => TextDraftInput.ReadAsync(stream)); }
    [Fact]
    public async Task CapacityCancellationAndNoSilentTruncation()
    {
        TextDraftInput.Validate(new("", string.Concat(Enumerable.Repeat("e\u0301", 20000))));
        Assert.Throws<InvalidDataException>(() => TextDraftInput.Validate(new("", new string('字', 20001))));
        Assert.Throws<InvalidDataException>(() => TextDraftInput.Validate(new(new string('字', 121), "")));
        using var stream = new MemoryStream(new byte[TextDraftInput.MaxBytes + 1]); await Assert.ThrowsAsync<InvalidDataException>(() => TextDraftInput.ReadAsync(stream));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); using var input = new MemoryStream([65]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TextDraftInput.ReadAsync(input, cancel.Token));
    }
}
