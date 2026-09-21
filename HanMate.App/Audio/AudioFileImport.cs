using HanMate.Core.Audio;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Audio;

public static class AudioFileImport
{
    public static async Task<AudioDraft> ImportAsync(LocalAudioStore store, AudioTarget target, Stream input, string cacheDirectory, CancellationToken token)
    {
        // No draft is registered until validation and native decoding have completely succeeded.
        var directory = Path.Combine(cacheDirectory, "decode-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "input"); var output = Path.Combine(directory, "decoded.wav");
        try
        {
            await using (var file = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new byte[65536]; int length; long total = 0;
                while ((length = await input.ReadAsync(bytes, token)) != 0)
                { total += length; if (total > PcmWave.MaximumBytes) throw new InvalidDataException("Audio exceeds 100 MiB."); await file.WriteAsync(bytes.AsMemory(0, length), token); }
                await file.FlushAsync(token);
            }
            string selected;
            using (var file = File.OpenRead(source))
            {
                var head = new byte[4]; file.ReadExactly(head); file.Position = 0;
                if (head.AsSpan().SequenceEqual("RIFF"u8)) { PcmWave.Inspect(file); selected = source; }
                else
                {
                    var info = CompressedAudio.Inspect(file);
                    selected = source + "." + info.Container;
                    // Some platform decoders require a container hint; the extension follows validated bytes.
                    file.Dispose(); File.Move(source, selected);
                    await NativeAudioDecoder.DecodeAsync(selected, output, info, token); selected = output;
                }
            }
            token.ThrowIfCancellationRequested();
            await using var decoded = File.OpenRead(selected);
            return await store.ImportAsync(target, decoded, token);
        }
        finally
        {
            // Only this operation's randomly named cache directory, never a user's input or saved media.
            foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
