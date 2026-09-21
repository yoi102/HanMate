using System.Text;

namespace HanMate.Core.Audio;

public sealed record WaveInfo(int SampleRate, int Channels, long DataOffset, long DataLength, long DurationMs);

/// <summary>Bounded RIFF parser. PCM16 samples have no compressed decoder state; every frame is checked structurally.</summary>
public static class PcmWave
{
    public const long MaximumBytes = 100L * 1024 * 1024;
    public static WaveInfo Inspect(Stream stream)
    {
        if (!stream.CanSeek || stream.Length is < 44 or > MaximumBytes) throw new InvalidDataException("Audio size is invalid.");
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        string Tag() => Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Tag() != "RIFF" || reader.ReadUInt32() != stream.Length - 8 || Tag() != "WAVE")
            throw new InvalidDataException("A complete RIFF WAVE file is required.");
        int rate = 0, channels = 0, alignment = 0;
        long dataOffset = 0, dataLength = 0;
        bool format = false, data = false;
        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 8) throw new InvalidDataException("Truncated chunk.");
            var tag = Tag(); var length = reader.ReadUInt32(); var start = stream.Position;
            var end = checked(start + length + (length & 1));
            if (end > stream.Length) throw new InvalidDataException("Truncated audio.");
            if (tag == "fmt ")
            {
                if (format || length < 16) throw new InvalidDataException("Invalid format chunk.");
                format = true;
                if (reader.ReadUInt16() != 1) throw new InvalidDataException("Only PCM16 WAV is supported.");
                channels = reader.ReadUInt16(); rate = checked((int)reader.ReadUInt32());
                var bytesPerSecond = reader.ReadUInt32(); alignment = reader.ReadUInt16(); var bits = reader.ReadUInt16();
                if (channels is < 1 or > 2 || rate is < 8000 or > 192000 || bits != 16 || alignment != channels * 2 || bytesPerSecond != rate * alignment)
                    throw new InvalidDataException("Unsupported PCM parameters.");
            }
            else if (tag == "data")
            {
                if (data) throw new InvalidDataException("Multiple data chunks are unsupported.");
                data = true; dataOffset = start; dataLength = length;
            }
            stream.Position = end;
        }
        if (!format || !data || dataLength == 0 || dataLength % alignment != 0) throw new InvalidDataException("No complete audio frames.");
        var duration = dataLength * 1000 / (rate * alignment);
        if (duration < 1) throw new InvalidDataException("Empty audio duration.");
        stream.Position = 0;
        return new(rate, channels, dataOffset, dataLength, duration);
    }

    public static void WriteHeader(Stream stream, int sampleRate, int channels, long dataLength)
    {
        if (dataLength < 0 || dataLength > MaximumBytes - 44 || dataLength % (channels * 2) != 0) throw new ArgumentOutOfRangeException(nameof(dataLength));
        stream.Position = 0;
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write((uint)(dataLength + 36)); writer.Write("WAVEfmt "u8);
        writer.Write(16u); writer.Write((ushort)1); writer.Write((ushort)channels); writer.Write((uint)sampleRate);
        writer.Write((uint)(sampleRate * channels * 2)); writer.Write((ushort)(channels * 2)); writer.Write((ushort)16);
        writer.Write("data"u8); writer.Write((uint)dataLength);
    }
}
