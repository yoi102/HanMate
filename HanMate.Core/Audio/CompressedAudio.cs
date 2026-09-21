using System.Buffers.Binary;
using System.Text;

namespace HanMate.Core.Audio;

public sealed record CompressedAudioInfo(string Container, int SampleRate, int Channels);

/// <summary>Conservative bounded container validation before invoking native decoders. This is not a decoder.</summary>
public static class CompressedAudio
{
    public static CompressedAudioInfo Inspect(Stream input)
    {
        if (!input.CanSeek || input.Length is < 16 or > PcmWave.MaximumBytes) throw Invalid();
        try
        {
            input.Position = 0; var header = Read(input, 12);
            if (header.AsSpan(4, 4).SequenceEqual("ftyp"u8)) return M4a(input);
            return Mp3(input);
        }
        catch (Exception e) when (e is EndOfStreamException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException)
        { throw new InvalidDataException("Invalid compressed audio.", e); }
        finally { input.Position = 0; }
    }
    private static InvalidDataException Invalid() => new("Unsupported or incomplete compressed audio.");
    private static byte[] Read(Stream input, int count) { var bytes = new byte[count]; input.ReadExactly(bytes); return bytes; }
    private static uint U32(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt32BigEndian(b);
    private static CompressedAudioInfo Mp3(Stream input)
    {
        input.Position = 0; var start = Read(input, 10);
        long end = input.Length;
        if (end >= 128) { input.Position = end - 128; if (Read(input, 3).AsSpan().SequenceEqual("TAG"u8)) end -= 128; }
        input.Position = 0;
        if (start.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            if (start[3] is < 2 or > 4 || start[4] != 0 || start.AsSpan(6, 4).ToArray().Any(b => b >= 128)) throw Invalid();
            var size = (start[6] << 21) | (start[7] << 14) | (start[8] << 7) | start[9];
            if (size > 1024 * 1024) throw Invalid();
            input.Position = 10L + size + (start[3] == 4 && (start[5] & 16) != 0 ? 10 : 0);
        }
        int frames = 0, rate = 0, channels = 0;
        int[] rates = [44100, 48000, 32000], mpeg1 = [0,32,40,48,56,64,80,96,112,128,160,192,224,256,320], mpeg2 = [0,8,16,24,32,40,48,56,64,80,96,112,128,144,160];
        while (input.Position < end)
        {
            var offset = input.Position;
            if (end - offset < 4) throw Invalid();
            var h = U32(Read(input, 4));
            var version = (int)((h >> 19) & 3); var layer = (h >> 17) & 3;
            var bit = (int)((h >> 12) & 15); var sample = (int)((h >> 10) & 3);
            if ((h & 0xffe00000) != 0xffe00000 || version == 1 || layer != 1 || bit is 0 or 15 || sample == 3) throw Invalid();
            var currentRate = rates[sample] / (version == 3 ? 1 : version == 2 ? 2 : 4);
            var currentChannels = (h >> 6 & 3) == 3 ? 1 : 2;
            if (frames > 0 && (rate != currentRate || channels != currentChannels)) throw Invalid();
            rate = currentRate; channels = currentChannels;
            var kbps = (version == 3 ? mpeg1 : mpeg2)[bit];
            var length = (version == 3 ? 144000 : 72000) * kbps / rate + (int)(h >> 9 & 1);
            if (offset + length > end) throw Invalid();
            input.Position = offset + length; frames++;
            if ((long)frames * (version == 3 ? 1152 : 576) * channels * 2 > PcmWave.MaximumBytes - 44) throw Invalid();
        }
        if (frames < 2 || rate < 8000) throw Invalid();
        return new("mp3", rate, channels);
    }

    private sealed record Box(string Kind, long Start, long End);
    private static List<Box> Boxes(Stream input, long start, long end)
    {
        var list = new List<Box>();
        while (start < end)
        {
            if (end - start < 8 || list.Count >= 10000) throw Invalid();
            input.Position = start; var h = Read(input, 8); long length = U32(h); var header = 8;
            if (length == 1) { length = checked((long)BinaryPrimitives.ReadUInt64BigEndian(Read(input, 8))); header = 16; }
            if (length == 0) length = end - start;
            if (length < header || length > end - start) throw Invalid();
            list.Add(new(Encoding.ASCII.GetString(h, 4, 4), start + header, start + length)); start += length;
        }
        return list;
    }
    private static Box One(IEnumerable<Box> boxes, string kind)
    { var items = boxes.Where(b => b.Kind == kind).ToArray(); return items.Length == 1 ? items[0] : throw Invalid(); }
    private static byte[] Bytes(Stream input, Box box, int maximum = 1024 * 1024)
    { if (box.End - box.Start > maximum) throw Invalid(); input.Position = box.Start; return Read(input, checked((int)(box.End - box.Start))); }
    private static CompressedAudioInfo M4a(Stream input)
    {
        var top = Boxes(input, 0, input.Length);
        if (top.Any(b => b.Kind is "moof" or "sidx")) throw Invalid();
        var media = One(top, "mdat"); var movie = One(top, "moov");
        var track = One(Boxes(input, movie.Start, movie.End), "trak"); // Audio-only; reject extra audio/video tracks.
        var mdia = One(Boxes(input, track.Start, track.End), "mdia"); var children = Boxes(input, mdia.Start, mdia.End);
        var handler = Bytes(input, One(children, "hdlr"));
        if (handler.Length < 12 || !handler.AsSpan(8, 4).SequenceEqual("soun"u8)) throw Invalid();
        var minf = One(children, "minf"); var mediaInfo = Boxes(input, minf.Start, minf.End);
        var dinf = One(mediaInfo, "dinf"); var dref = One(Boxes(input, dinf.Start, dinf.End), "dref");
        input.Position = dref.Start; var reference = Read(input, 8);
        if (dref.End - dref.Start < 8 || U32(reference) != 0 || U32(reference.AsSpan(4)) != 1) throw Invalid();
        var refs = Boxes(input, dref.Start + 8, dref.End); var self = One(refs, "url ");
        if (refs.Count != 1 || !Bytes(input, self).AsSpan().SequenceEqual(new byte[] { 0, 0, 0, 1 })) throw Invalid();
        var table = One(mediaInfo, "stbl");
        var tables = Boxes(input, table.Start, table.End); var desc = One(tables, "stsd");
        input.Position = desc.Start; var prefix = Read(input, 8);
        if (U32(prefix) != 0 || U32(prefix.AsSpan(4)) != 1) throw Invalid();
        var entry = One(Boxes(input, desc.Start + 8, desc.End), "mp4a");
        input.Position = entry.Start; var audio = Read(input, 28);
        if (entry.End - entry.Start < 28 || BinaryPrimitives.ReadUInt16BigEndian(audio.AsSpan(8)) != 0
            || BinaryPrimitives.ReadUInt16BigEndian(audio.AsSpan(6)) != 1) throw Invalid();
        var esds = Bytes(input, One(Boxes(input, entry.Start + 28, entry.End), "esds"));
        if (esds.Length < 4 || U32(esds) != 0) throw Invalid();
        var es = Descriptor(esds.AsSpan(4), 3); if (es.Length < 3) throw Invalid();
        var flags = es[2]; int offset = 3;
        if ((flags & 128) != 0) offset += 2;
        if ((flags & 64) != 0) { if (offset >= es.Length) throw Invalid(); offset += 1 + es[offset]; }
        if ((flags & 32) != 0) offset += 2;
        var config = Descriptor(es[offset..], 4);
        if (config.Length < 13 || config[0] != 0x40 || (config[1] >> 2) != 5) throw Invalid();
        var asc = Descriptor(config[13..], 5);
        // AAC-LC, indexed frequency, mono/stereo, 1024-sample frames, no extension/core dependency.
        if (asc.Length is < 2 or > 16 || asc[0] >> 3 != 2 || (asc[1] & 7) != 0) throw Invalid();
        // Common LC encoders explicitly signal syncExtensionType=0x2b7, AOT=5, sbrPresentFlag=0.
        if (asc[2..].IndexOfAnyExcept((byte)0) >= 0 && !asc[2..].SequenceEqual(new byte[] { 0x56, 0xe5, 0 })) throw Invalid();
        var frequency = ((asc[0] & 7) << 1) | (asc[1] >> 7); var channels = (asc[1] >> 3) & 15;
        int[] rates = [96000,88200,64000,48000,44100,32000,24000,22050,16000,12000,11025,8000,7350];
        if (frequency >= 12 || channels is < 1 or > 2) throw Invalid();
        ValidateSamples(input, tables, media, channels);
        return new("m4a", rates[frequency], channels);
    }
    private static ReadOnlySpan<byte> Descriptor(ReadOnlySpan<byte> data, byte tag)
    {
        if (data.Length < 2 || data[0] != tag) throw Invalid();
        int length = 0, offset = 1;
        for (var i = 0; i < 4; i++)
        {
            if (offset >= data.Length) throw Invalid();
            var b = data[offset++]; length = (length << 7) | (b & 127);
            if ((b & 128) == 0) { if (length > data.Length - offset) throw Invalid(); return data.Slice(offset, length); }
        }
        throw Invalid();
    }
    private static void ValidateSamples(Stream input, List<Box> tables, Box media, int channels)
    {
        var sizes = Bytes(input, One(tables, "stsz"), 4 * 1024 * 1024);
        if (sizes.Length < 12 || U32(sizes) != 0) throw Invalid();
        var fixedSize = U32(sizes.AsSpan(4)); var count = U32(sizes.AsSpan(8));
        if (count is 0 or > 1000000 || sizes.Length != 12L + (fixedSize == 0 ? count * 4L : 0)) throw Invalid();
        if ((long)count * 1024 * channels * 2 > PcmWave.MaximumBytes - 44) throw Invalid();
        var offsetsBox = tables.SingleOrDefault(b => b.Kind is "stco" or "co64") ?? throw Invalid();
        var offsets = Bytes(input, offsetsBox); var mapping = Bytes(input, One(tables, "stsc"));
        if (offsets.Length < 8 || mapping.Length < 8 || U32(offsets) != 0 || U32(mapping) != 0) throw Invalid();
        var chunks = U32(offsets.AsSpan(4)); var rules = U32(mapping.AsSpan(4)); var width = offsetsBox.Kind == "co64" ? 8 : 4;
        if (chunks == 0 || rules == 0 || offsets.Length != 8L + chunks * width || mapping.Length != 8L + rules * 12 || U32(mapping.AsSpan(8)) != 1) throw Invalid();
        uint sample = 0, rule = 0; long next = media.Start;
        for (uint chunk = 1; chunk <= chunks; chunk++)
        {
            if (rule + 1 < rules && U32(mapping.AsSpan(8 + (int)(rule + 1) * 12)) == chunk) rule++;
            var r = 8 + (int)rule * 12;
            if (U32(mapping.AsSpan(r + 8)) != 1) throw Invalid();
            if (rule + 1 < rules && U32(mapping.AsSpan(r + 12)) <= chunk) throw Invalid();
            var samples = U32(mapping.AsSpan(r + 4)); if (samples == 0 || samples > count - sample) throw Invalid();
            var o = offsets.AsSpan(8 + (int)(chunk - 1) * width);
            var position = width == 8 ? checked((long)BinaryPrimitives.ReadUInt64BigEndian(o)) : U32(o);
            if (position != next) throw Invalid();
            for (uint i = 0; i < samples; i++, sample++)
            { var length = fixedSize != 0 ? fixedSize : U32(sizes.AsSpan(12 + (int)sample * 4)); if (length == 0) throw Invalid(); next += length; }
            if (next > media.End) throw Invalid();
        }
        if (sample != count || next != media.End || rule + 1 != rules) throw Invalid();
    }
}
