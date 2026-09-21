using System.Diagnostics;
using HanMate.Core.Audio;

namespace HanMate.App.Audio;

/// <summary>Decodes a prevalidated private compressed file to bounded PCM16. Never opens a URL.</summary>
public static class NativeAudioDecoder
{
    public static async Task DecodeAsync(string source, string destination, CompressedAudioInfo info, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
#if WINDOWS
            await WindowsAsync(source, destination, info, timeout.Token);
#elif ANDROID
            await Task.Run(() => AndroidDecode(source, destination, info, timeout.Token), timeout.Token);
#elif IOS || MACCATALYST
            await Task.Run(() => AppleDecode(source, destination, info, timeout.Token), timeout.Token);
#else
            await Task.CompletedTask; throw new PlatformNotSupportedException();
#endif
            timeout.Token.ThrowIfCancellationRequested();
            using var output = File.OpenRead(destination); var wave = PcmWave.Inspect(output);
            if (wave.SampleRate != info.SampleRate || wave.Channels != info.Channels) throw new InvalidDataException("Decoder format mismatch.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Audio decoding timed out."); }
    }

#if WINDOWS
    private static async Task WindowsAsync(string source, string destination, CompressedAudioInfo info, CancellationToken token)
    {
        using var inputFile = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var outputFile = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        using var input = inputFile.AsRandomAccessStream(); using var output = outputFile.AsRandomAccessStream();
        var profile = global::Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(global::Windows.Media.MediaProperties.AudioEncodingQuality.Auto);
        profile.Audio = global::Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm((uint)info.SampleRate, (uint)info.Channels, 16);
        var transcoder = new global::Windows.Media.Transcoding.MediaTranscoder { AlwaysReencode = true };
        var prepared = await transcoder.PrepareStreamTranscodeAsync(input, output, profile).AsTask(token);
        if (!prepared.CanTranscode) throw new InvalidDataException("Native decoder rejected this audio.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        var work = prepared.TranscodeAsync().AsTask(bounded.Token);
        try
        {
            while (!work.IsCompleted)
            {
                if (new FileInfo(destination).Length > PcmWave.MaximumBytes) throw new InvalidDataException("Decoded audio exceeds 100 MiB.");
                await Task.WhenAny(work, Task.Delay(30, token)); token.ThrowIfCancellationRequested();
            }
            await work;
        }
        finally
        {
            // Even after a size/cancellation failure, await native shutdown before deleting the private staging file.
            bounded.Cancel(); try { await work; } catch (OperationCanceledException) { }
        }
    }
#endif

#if ANDROID
    private static void AndroidDecode(string source, string destination, CompressedAudioInfo info, CancellationToken token)
    {
        using var extractor = new Android.Media.MediaExtractor(); extractor.SetDataSource(source);
        if (extractor.TrackCount != 1) throw new InvalidDataException("Exactly one audio track is required.");
        using var format = extractor.GetTrackFormat(0); var mime = format.GetString(Android.Media.MediaFormat.KeyMime);
        if (mime != (info.Container == "mp3" ? "audio/mpeg" : "audio/mp4a-latm")) throw new InvalidDataException("Codec mismatch.");
        format.SetInteger("pcm-encoding", (int)Android.Media.Encoding.Pcm16bit);
        using var decoder = Android.Media.MediaCodec.CreateDecoderByType(mime);
        decoder.Configure(format, null, null, Android.Media.MediaCodecConfigFlags.None); decoder.Start();
        using var output = new PcmOutput(destination, info); using var bufferInfo = new Android.Media.MediaCodec.BufferInfo();
        var scratch = new byte[65536]; var inputEnded = false; var outputEnded = false; var changed = false; var progress = Stopwatch.StartNew();
        extractor.SelectTrack(0);
        try
        {
            while (!outputEnded)
            {
                token.ThrowIfCancellationRequested();
                if (progress.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Native decoder stalled.");
                if (!inputEnded)
                {
                    var index = decoder.DequeueInputBuffer(10000);
                    if (index >= 0)
                    {
                        using var buffer = decoder.GetInputBuffer(index) ?? throw new InvalidDataException("No decoder input buffer."); buffer.Clear();
                        var length = extractor.ReadSampleData(buffer, 0);
                        if (length < 0) { decoder.QueueInputBuffer(index, 0, 0, 0, Android.Media.MediaCodecBufferFlags.EndOfStream); inputEnded = true; }
                        else { decoder.QueueInputBuffer(index, 0, length, extractor.SampleTime, Android.Media.MediaCodecBufferFlags.None); extractor.Advance(); }
                        progress.Restart();
                    }
                }
                var decoded = decoder.DequeueOutputBuffer(bufferInfo, 10000);
                if (decoded == (int)Android.Media.MediaCodecInfoState.OutputFormatChanged)
                {
                    using var actual = decoder.OutputFormat;
                    if (actual.GetInteger(Android.Media.MediaFormat.KeySampleRate) != info.SampleRate || actual.GetInteger(Android.Media.MediaFormat.KeyChannelCount) != info.Channels
                        || (actual.ContainsKey("pcm-encoding") && actual.GetInteger("pcm-encoding") != (int)Android.Media.Encoding.Pcm16bit)) throw new InvalidDataException("Unsupported decoded PCM format.");
                    changed = true; progress.Restart();
                }
                else if (decoded >= 0)
                {
                    try
                    {
                        if (bufferInfo.Size > 0 && (bufferInfo.Flags & Android.Media.MediaCodecBufferFlags.CodecConfig) == 0)
                        {
                            if (!changed) throw new InvalidDataException("Decoder did not report its PCM format.");
                            using var buffer = decoder.GetOutputBuffer(decoded) ?? throw new InvalidDataException("No decoder output buffer.");
                            buffer.Position(bufferInfo.Offset); buffer.Limit(bufferInfo.Offset + bufferInfo.Size);
                            for (var remaining = bufferInfo.Size; remaining > 0;)
                            { var bytes = Math.Min(remaining, scratch.Length); buffer.Get(scratch, 0, bytes); output.Write(scratch.AsSpan(0, bytes)); remaining -= bytes; }
                        }
                        outputEnded = (bufferInfo.Flags & Android.Media.MediaCodecBufferFlags.EndOfStream) != 0; progress.Restart();
                    }
                    finally { decoder.ReleaseOutputBuffer(decoded, false); }
                }
            }
            output.Complete();
        }
        finally { try { decoder.Stop(); } finally { decoder.Release(); } }
    }
#endif

#if IOS || MACCATALYST
    private static void AppleDecode(string source, string destination, CompressedAudioInfo info, CancellationToken token)
    {
        using var url = Foundation.NSUrl.FromFilename(source);
        using var input = new AVFoundation.AVAudioFile(url, AVFoundation.AVAudioCommonFormat.PCMInt16, true, out var error);
        if (error is not null) { error.Dispose(); throw new InvalidDataException("Native decoder rejected this audio."); }
        using var format = input.ProcessingFormat;
        if (format.SampleRate != info.SampleRate || format.ChannelCount != info.Channels || !format.Interleaved) throw new InvalidDataException("Decoder format mismatch.");
        using var buffer = new AVFoundation.AVAudioPcmBuffer(format, 4096); using var output = new PcmOutput(destination, info);
        var bytes = new byte[4096 * info.Channels * 2]; long frames = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!input.ReadIntoBuffer(buffer, 4096, out var readError)) { readError?.Dispose(); throw new InvalidDataException("Audio decode failed."); }
            if (buffer.FrameLength == 0) break;
            var count = checked((int)buffer.FrameLength * info.Channels * 2);
            System.Runtime.InteropServices.Marshal.Copy(System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer.Int16ChannelData), bytes, 0, count);
            output.Write(bytes.AsSpan(0, count)); frames += buffer.FrameLength;
        }
        if (frames != input.Length) throw new InvalidDataException("Incomplete decoded audio.");
        output.Complete();
    }
#endif

#if ANDROID || IOS || MACCATALYST
    private sealed class PcmOutput : IDisposable
    {
        private readonly FileStream _file; private readonly CompressedAudioInfo _info; private long _bytes;
        public PcmOutput(string path, CompressedAudioInfo info)
        { _info = info; _file = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read); PcmWave.WriteHeader(_file, info.SampleRate, info.Channels, 0); }
        public void Write(ReadOnlySpan<byte> bytes)
        { if (_bytes + bytes.Length > PcmWave.MaximumBytes - 44) throw new InvalidDataException("Decoded audio exceeds 100 MiB."); _file.Write(bytes); _bytes += bytes.Length; }
        public void Complete() { PcmWave.WriteHeader(_file, _info.SampleRate, _info.Channels, _bytes); _file.Flush(true); }
        public void Dispose() => _file.Dispose();
    }
#endif
}
