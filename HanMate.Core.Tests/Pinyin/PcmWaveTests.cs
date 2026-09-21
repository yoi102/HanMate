using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Pinyin;

public sealed class PcmWaveTests
{
    [Theory]
    [InlineData(8000, 1)]
    [InlineData(44100, 1)]
    [InlineData(48000, 2)]
    public void ReadsActualPcmFramesAndDuration(int rate, int channels)
    {
        using var stream = Wave(rate, channels);
        var info = PcmWave.Inspect(stream);
        Assert.Equal(rate, info.SampleRate); Assert.Equal(channels, info.Channels);
        Assert.Equal(1000, info.DurationMs); Assert.Equal(rate * channels * 2, info.DataLength); Assert.Equal(0, stream.Position);
    }

    [Theory]
    [InlineData(0, 0)] // RIFF identity
    [InlineData(4, 0)] // RIFF length
    [InlineData(20, 3)] // float instead of PCM
    [InlineData(22, 3)] // three channels
    [InlineData(28, 0)] // inconsistent byte rate
    [InlineData(32, 4)] // inconsistent frame alignment
    [InlineData(34, 8)] // unsupported bit depth
    [InlineData(40, 1)] // truncated/misaligned data size
    public void RejectsMisleadingAndDamagedHeaders(int offset, byte value)
    {
        using var stream = Wave(8000, 1); stream.GetBuffer()[offset] = value;
        Assert.Throws<InvalidDataException>(() => PcmWave.Inspect(stream));
    }

    [Fact]
    public void RejectsEmptyAndTruncatedContainer()
    {
        using var stream = new MemoryStream(); PcmWave.WriteHeader(stream, 44100, 1, 0);
        Assert.Throws<InvalidDataException>(() => PcmWave.Inspect(stream));
        using var good = Wave(8000, 1); good.SetLength(good.Length - 1);
        Assert.Throws<InvalidDataException>(() => PcmWave.Inspect(good));
    }

    [Fact]
    public void AllowsBoundedUnknownMetadataChunksButNotDuplicateData()
    {
        using var wave = Wave(8000, 1); var bytes = wave.ToArray();
        using var stream = new MemoryStream(); stream.Write(bytes); stream.Write("JUNK"u8); stream.Write(BitConverter.GetBytes(3)); stream.Write(new byte[4]);
        stream.Position = 4; stream.Write(BitConverter.GetBytes((uint)stream.Length - 8));
        Assert.Equal(1000, PcmWave.Inspect(stream).DurationMs);
        stream.Position = bytes.Length; stream.Write("data"u8);
        Assert.Throws<InvalidDataException>(() => PcmWave.Inspect(stream));
    }

    private static MemoryStream Wave(int rate, int channels)
    {
        var stream = new MemoryStream(); PcmWave.WriteHeader(stream, rate, channels, rate * channels * 2);
        stream.Write(new byte[rate * channels * 2]); stream.Position = 0; return stream;
    }
}
