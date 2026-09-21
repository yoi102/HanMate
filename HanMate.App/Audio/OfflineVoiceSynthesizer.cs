using HanMate.Infrastructure.Voices;
using HanMate.Core.Audio;

namespace HanMate.App.Audio;

/// <summary>Production CPU inference, also exercised by the independent desktop probe.</summary>
public sealed class OfflineVoiceSynthesizer(VoicePackStore packs)
{
    private static readonly SemaphoreSlim EngineGate = new(1, 1);
    private static readonly object LexiconLock = new();
    private static (string Path, DateTime Written, HashSet<string> Characters)? _lexicon;
#if WINDOWS || ANDROID || VOICE_PROBE
    private static CheckedTts? _engine;
    private static string? _engineKey;
    private static (string Source, string File, MeloPronunciationLexicon Lexicon)? _meloFrontend;
#endif
    public static async Task ReleaseCacheAsync()
    {
        await EngineGate.WaitAsync().ConfigureAwait(false);
        try
        {
#if WINDOWS || ANDROID || VOICE_PROBE
            _engine?.Dispose(); _engine = null; _engineKey = null;
            if (_meloFrontend is { } frontend) File.Delete(frontend.File);
            _meloFrontend = null;
#endif
            lock (LexiconLock) _lexicon = null;
        }
        finally { EngineGate.Release(); }
    }
    public static bool Supported =>
#if WINDOWS || ANDROID || VOICE_PROBE
        true;
#else
        false;
#endif
    public Task<byte[]> GenerateAsync(string text, int speaker, CancellationToken token) => packs.UseAsync(speaker, path => Task.Run(() => Generate(path, text, speaker, token), token), token);
    public Task<byte[]> GeneratePinyinAsync(string phonemes, int speaker, CancellationToken token) => packs.UseAsync(speaker,
        path => Task.Run(() => Generate(path, phonemes, speaker, token, explicitPinyin: true), token), token);
    public async Task ValidateAsync(string text, int speaker, CancellationToken token)
    {
        await packs.UseAsync(speaker, path => Task.Run(() =>
        {
            var lexicon = ReadCharacters(path);
            OfflineVoiceInput.Validate(text, lexicon.Contains, token);
            return true;
        }, token), token);
    }
    private static HashSet<string> ReadCharacters(string path)
    {
        var file = Path.Combine(path, File.Exists(Path.Combine(path, "voices.bin")) ? "lexicon-zh.txt" : "lexicon.txt");
        var written = File.GetLastWriteTimeUtc(file);
        lock (LexiconLock)
        {
            if (_lexicon is { } cached && cached.Path == file && cached.Written == written) return cached.Characters;
            var characters = File.ReadLines(file).Select(line => line.Split(' ', 2)[0]).ToHashSet(StringComparer.Ordinal);
            _lexicon = (file, written, characters); return characters;
        }
    }
    private static byte[] Generate(string path, string text, int speaker, CancellationToken token, bool explicitPinyin = false)
    {
        EngineGate.Wait(token);
        try { return GenerateCore(path, text, speaker, token, explicitPinyin); }
        finally { EngineGate.Release(); }
    }
    private static byte[] GenerateCore(string path, string text, int speaker, CancellationToken token, bool explicitPinyin)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500 || text.Contains('\0')) throw new InvalidDataException("Invalid speech chunk.");
        token.ThrowIfCancellationRequested();
#if WINDOWS || ANDROID || VOICE_PROBE
        if (!explicitPinyin) text = OfflineVoiceInput.Prepare(text, ReadCharacters(path).Contains);
        var config = new SherpaOnnx.OfflineTtsConfig();
        if (File.Exists(Path.Combine(path, "voices.bin")))
        {
            if (explicitPinyin) throw new InvalidDataException("Explicit pinyin requires Melo.");
            config.Model.Kokoro.Model = Path.Combine(path, "model.onnx");
            config.Model.Kokoro.Lang = "zh";
            config.Model.Kokoro.Voices = Path.Combine(path, "voices.bin");
            config.Model.Kokoro.Tokens = Path.Combine(path, "tokens.txt");
            config.Model.Kokoro.Lexicon = Path.Combine(path, "lexicon-zh.txt");
            config.Model.Kokoro.DictDir = Path.Combine(path, "dict");
            config.Model.Kokoro.DataDir = Path.Combine(path, "espeak-ng-data");
            config.RuleFsts = string.Join(",", new[] { "phone-zh.fst", "date-zh.fst", "number-zh.fst" }.Select(f => Path.Combine(path, f)));
        }
        else
        {
            var frontend = MeloFrontend(path);
            if (explicitPinyin) text = frontend.Lexicon.Encode(text);
            text = MeloPronunciationLexicon.WithBoundaries(text);
            config.Model.Vits.Model = Path.Combine(path, "model.onnx");
            config.Model.Vits.Lexicon = frontend.File;
            config.Model.Vits.Tokens = Path.Combine(path, "tokens.txt");
            config.Model.Vits.DictDir = Path.Combine(path, "dict");
            config.RuleFsts = string.Join(",", new[] { "phone.fst", "date.fst", "number.fst", "new_heteronym.fst" }.Select(f => Path.Combine(path, f)));
        }
        config.Model.NumThreads = 2;
        // Keep one native model resident. Text and exact pinyin share the same fixed
        // frontend, so selecting a different word no longer reloads the model.
        var key = path;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (_engine is null || _engineKey != key)
        {
            _engine?.Dispose(); _engine = null; _engineKey = null;
            _engine = new CheckedTts(config); _engineKey = key;
            SpeechDiagnostics.Write($"model-load ms={timer.ElapsedMilliseconds}");
        }
        var engine = _engine;
        if (engine.NumSpeakers <= speaker || engine.SampleRate is < 8000 or > 48000) throw new InvalidDataException("Voice engine failed to load.");
        token.ThrowIfCancellationRequested();
        timer.Restart();
        var audio = engine.GenerateWithCallback(text, 1, speaker, (_, _) => token.IsCancellationRequested ? 0 : 1);
        try
        {
            token.ThrowIfCancellationRequested();
            if (audio.Handle == IntPtr.Zero || audio.NumSamples is <= 0 || audio.NumSamples > audio.SampleRate * 180) throw new InvalidDataException("Voice produced no usable audio.");
            var samples = audio.Samples;
            SpeechDiagnostics.Write($"synthesis chars={text.Length} ms={timer.ElapsedMilliseconds} samples={samples.Length} rate={audio.SampleRate} invalid={samples.Count(s => !float.IsFinite(s))}");
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write("RIFF"u8); writer.Write(36 + samples.Length * 2); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(audio.SampleRate);
            writer.Write(audio.SampleRate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples.Length * 2);
            foreach (var sample in samples)
            { if (!float.IsFinite(sample)) throw new InvalidDataException("Invalid generated sample."); writer.Write((short)(Math.Clamp(sample, -1, 1) * short.MaxValue)); }
            return output.ToArray();
        }
        finally { audio.Dispose(); }
#else
        throw new PlatformNotSupportedException();
#endif
    }
#if WINDOWS || ANDROID || VOICE_PROBE
    private static (string File, MeloPronunciationLexicon Lexicon) MeloFrontend(string path)
    {
        if (_meloFrontend is { } cached && cached.Source == path && File.Exists(cached.File)) return (cached.File, cached.Lexicon);
        var lexicon = new MeloPronunciationLexicon(File.ReadLines(Path.Combine(path, "tokens.txt"))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]));
        var file = Path.Combine(Path.GetTempPath(), "hanmate-melo-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            // Never modify the hash-verified voice pack. Reserved aliases append exact
            // phone/tone pairs to a temporary copy of the normal Chinese lexicon.
            File.WriteAllText(file, File.ReadAllText(Path.Combine(path, "lexicon.txt")) + "\n" + lexicon.Entries,
                new System.Text.UTF8Encoding(false));
        }
        catch { File.Delete(file); throw; }
        if (_meloFrontend is { } previous) File.Delete(previous.File);
        _meloFrontend = (path, file, lexicon);
        return (file, lexicon);
    }

    // The pinned managed constructor does not reject a null native handle. Check
    // creation before calling native accessors, which otherwise dereference null.
    private sealed class CheckedTts : IDisposable
    {
        private IntPtr _handle;
        public CheckedTts(SherpaOnnx.OfflineTtsConfig config)
        {
            _handle = Create(ref config);
            if (_handle == IntPtr.Zero) throw new InvalidDataException("Voice engine could not initialize.");
        }
        public int NumSpeakers => Speakers(_handle);
        public int SampleRate => Rate(_handle);
        public SherpaOnnx.OfflineTtsGeneratedAudio GenerateWithCallback(string text, float speed, int speaker, SherpaOnnx.OfflineTtsCallback callback)
        {
            var handle = Generate(_handle, System.Text.Encoding.UTF8.GetBytes(text + "\0"), speaker, speed, callback);
            GC.KeepAlive(callback);
            return new(handle);
        }
        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero) Destroy(handle);
        }
        private const string Library = "sherpa-onnx-c-api";
        [System.Runtime.InteropServices.DllImport(Library, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl, EntryPoint = "SherpaOnnxCreateOfflineTts")]
        private static extern IntPtr Create(ref SherpaOnnx.OfflineTtsConfig config);
        [System.Runtime.InteropServices.DllImport(Library, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl, EntryPoint = "SherpaOnnxDestroyOfflineTts")]
        private static extern void Destroy(IntPtr handle);
        [System.Runtime.InteropServices.DllImport(Library, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl, EntryPoint = "SherpaOnnxOfflineTtsNumSpeakers")]
        private static extern int Speakers(IntPtr handle);
        [System.Runtime.InteropServices.DllImport(Library, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl, EntryPoint = "SherpaOnnxOfflineTtsSampleRate")]
        private static extern int Rate(IntPtr handle);
        [System.Runtime.InteropServices.DllImport(Library, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl, EntryPoint = "SherpaOnnxOfflineTtsGenerateWithCallback")]
        private static extern IntPtr Generate(IntPtr handle, byte[] text, int speaker, float speed, SherpaOnnx.OfflineTtsCallback callback);
    }
#endif
}

internal static class SpeechDiagnostics
{
    private static readonly object Sync = new();
    // Never log spoken text or user document identities.
    public static void Write(string message)
    {
#if ANDROID
        Android.Util.Log.Info("HanMateSpeech", message);
        try
        {
            lock (Sync)
            {
                var path = Path.Combine(Android.App.Application.Context.CacheDir!.AbsolutePath, "speech-diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length > 65536) File.Delete(path);
                File.AppendAllText(path, DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
        }
        catch (IOException) { }
#else
        System.Diagnostics.Trace.WriteLine("HanMateSpeech " + message);
#endif
    }
}
