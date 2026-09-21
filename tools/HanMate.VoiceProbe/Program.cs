using System.Security.Cryptography;
using System.Text.Json;
using HanMate.App.Audio;
using HanMate.Infrastructure.Voices;
using HanMate.Core.Audio;
using System.Diagnostics;

var root = Path.Combine(Path.GetTempPath(), "HanMate-voice-probe");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var store = new VoicePackStore(root, http);
await store.InstallAsync(null);
await store.SelectAsync(66);
var synth = new OfflineVoiceSynthesizer(store);
if (args.Contains("--pinyin-review"))
{
    var directory = Path.Combine(root, "pinyin-review"); Directory.CreateDirectory(directory);
    var samples = new List<object>();
    foreach (var (id, phones) in new[] {
        ("bo", "b o1 #0"), ("po", "p o1 #0"), ("mo", "m o1 #0"), ("fo", "f o1 #0"),
        ("de", "d e1 #0"), ("le", "l e1 #0"), ("v", "^ v1 #0"), ("ong", "^ ong1 #0"),
        ("zi", "z ii1 #0"), ("zhi", "zh iii1 #0"), ("bank", "^ in2 #0 h ang2 #0"),
        ("mother", "m a1 #0 m a5 #0"), ("four-tones", "m a1 #0 m a2 #0 m a3 #0 m a4 #0") })
    {
        var wav = await synth.GeneratePinyinAsync(phones, 66, CancellationToken.None);
        var metrics = Inspect(wav); var path = Path.Combine(directory, id + ".wav");
        await File.WriteAllBytesAsync(path, wav);
        samples.Add(new { id, phones, metrics, path, sha256 = Convert.ToHexStringLower(SHA256.HashData(wav)) });
        Console.WriteLine($"Generated pinyin {id}: {wav.Length} bytes");
    }
    try { await synth.GeneratePinyinAsync("b invalid1 #0", 66, CancellationToken.None); throw new Exception("Invalid phones accepted."); }
    catch (InvalidDataException) { }
    using var pinyinCancelled = new CancellationTokenSource(); pinyinCancelled.Cancel();
    try { await synth.GeneratePinyinAsync("b o1 #0", 66, pinyinCancelled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { }
    Inspect(await synth.GenerateAsync("你好。", 66, CancellationToken.None));
    await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new {
        status = "PASS", scope = "Explicit phoneme synthesis, signal checks, rejection, cancellation and ordinary-text recovery. Listening NOT RUN.",
        samples }, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
if (args.Length == 2 && args[0] == "--review-samples")
{
    using var csv = new Microsoft.VisualBasic.FileIO.TextFieldParser(args[1]);
    csv.SetDelimiters(","); csv.HasFieldsEnclosedInQuotes = true;
    if (csv.ReadFields() is not { } header || header[0] != "caseId" || header[1] != "text") throw new InvalidDataException("Invalid review corpus.");
    var manifest = new List<object>(); var directory = Path.Combine(root, "review-samples"); Directory.CreateDirectory(directory);
    while (!csv.EndOfData)
    {
        var fields = csv.ReadFields()!; var id = fields[0]; var text = fields[1];
        if (id.Length > 40 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Invalid case ID.");
        foreach (var speaker in new[] { 66, 10 })
        {
            await synth.ValidateAsync(text, speaker, CancellationToken.None);
            var part = 0;
            foreach (var chunk in OfflineVoiceInput.Chunks(text))
            {
                var wav = await synth.GenerateAsync(chunk, speaker, CancellationToken.None); var metrics = Inspect(wav);
                var path = Path.Combine(directory, $"{id}-speaker-{speaker:D3}-part-{++part}.wav"); await File.WriteAllBytesAsync(path, wav);
                manifest.Add(new { id, text, speaker, part, path, metrics, sha256 = Convert.ToHexStringLower(SHA256.HashData(wav)), listeningReview = "NOT RUN" });
            }
        }
    }
    // Regression: preflight must reject a punctuation-only native chunk before any playback begins.
    try { await synth.ValidateAsync("你好" + new string('！', 100) + "你好", 66, CancellationToken.None); throw new Exception("Preflight accepted an unplayable chunk."); }
    catch (UnsupportedVoiceTextException) { }
    await synth.ValidateAsync("你好" + new string('\n', 100) + "你好", 66, CancellationToken.None);
    await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new { generatedAt = DateTimeOffset.UtcNow,
        model = store.Pack.Version, scope = "Review material, not a listening approval. Native preflight regression PASS.", samples = manifest }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Prepared {manifest.Count} WAV files for review at {directory}; native preflight regression PASS, listening NOT RUN.");
    return;
}
if (args.Contains("--soak"))
{
    // Fixed public test text only. Uses the same bounded chunks as the reader; no audio device playback.
    var minutes = 20;
    var corpus = new[] { "你好，我们一起学习汉语。", "重庆银行的行长喜欢音乐。", "今天是2026年9月19日，价格是3.14元。",
        string.Concat(Enumerable.Repeat("12345 ", 20)), "女儿穿着绿色的衣服。妈妈和爸爸一起看书。" };
    var process = Process.GetCurrentProcess();
    var samples = new List<object>(); var cancellations = new List<double>();
    var started = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew();
    var iteration = 0; double nextReport = 0;
    var output = Path.Combine(root, "soak.json");
    while (elapsed.Elapsed < TimeSpan.FromMinutes(minutes))
    {
        var caseId = iteration % corpus.Length;
        await synth.ValidateAsync(corpus[caseId], 66, CancellationToken.None);
        var watch = Stopwatch.StartNew(); var chunks = 0; long bytes = 0;
        foreach (var chunk in OfflineVoiceInput.Chunks(corpus[caseId]))
        {
            var wav = await synth.GenerateAsync(chunk, 66, CancellationToken.None);
            Inspect(wav); chunks++; bytes += wav.Length;
        }
        var generateMs = watch.Elapsed.TotalMilliseconds;
        if (iteration < 30)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            watch.Restart(); var observed = false;
            try { await synth.GenerateAsync("12345 12345 12345 12345 12345 12345 12345 12345", 66, stop.Token); }
            catch (OperationCanceledException) { observed = true; }
            if (!observed) throw new Exception("Soak cancellation was not observed.");
            cancellations.Add(watch.Elapsed.TotalMilliseconds);
            Inspect(await synth.GenerateAsync("停止后继续学习。", 66, CancellationToken.None));
        }
        process.Refresh();
        samples.Add(new { iteration, caseId, chunks, bytes, generateMs, elapsedSeconds = elapsed.Elapsed.TotalSeconds,
            privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64 });
        iteration++;
        if (elapsed.Elapsed.TotalSeconds >= nextReport)
        {
            await Save("RUNNING"); nextReport = elapsed.Elapsed.TotalSeconds + 60;
            Console.WriteLine($"Soak {elapsed.Elapsed.TotalMinutes:F1}/{minutes} minutes, {iteration} iterations, private {process.PrivateMemorySize64 / 1048576} MiB");
        }
        await Task.Delay(100);
    }
    await Save("PASS");
    Console.WriteLine($"PASS {iteration} iterations, {cancellations.Count} cancelled/recovered. Report: {output}");
    return;

    async Task Save(string status) => await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
    {
        status, started, ended = DateTimeOffset.UtcNow, elapsedSeconds = elapsed.Elapsed.TotalSeconds, samples, cancellations,
        model = store.Pack.Version, modelSha256 = store.Pack.Files.Single(f => f.Name == "model.onnx").Sha256,
        probeSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(OfflineVoiceSynthesizer).Assembly.Location))),
        environment = new { os = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription },
        scope = "Windows native offline synthesis, signal checks and cancellation; sampled memory, not peak, listening review, UI or device playback."
    }, new JsonSerializerOptions { WriteIndented = true }));
}
if (args.Contains("--audit"))
{
    var audit = new List<object>(); var timings = new List<double>();
    var process = Process.GetCurrentProcess();
    var initialMemory = process.PrivateMemorySize64;
    for (var speaker = 0; speaker < store.Pack.Speakers; speaker++)
    {
        var watch = Stopwatch.StartNew();
        var wav = await synth.GenerateAsync("你好，我们一起学习汉语。", speaker, CancellationToken.None);
        var metrics = Inspect(wav); timings.Add(watch.Elapsed.TotalMilliseconds);
        audit.Add(new { speaker, metrics, elapsedMs = watch.Elapsed.TotalMilliseconds });
        if ((speaker + 1) % 20 == 0) Console.WriteLine($"Verified {speaker + 1}/{store.Pack.Speakers} voices");
    }
    var corpus = new[] { "重庆银行的行长喜欢音乐。", "今天是2026年9月19日，价格是3.14元。", "女儿穿着绿色的衣服。", "你好（欢迎学习），价格３.１４元。" };
    var cases = new List<object>();
    foreach (var text in corpus)
    {
        var wav = await synth.GenerateAsync(text, 66, CancellationToken.None);
        cases.Add(new { text, metrics = Inspect(wav) });
    }
    foreach (var text in new[] { "你好👩‍👩‍👧‍👦，欢迎学习汉语。", "Hello，你好，ABC，123。", "你好𰻞", "！？" })
    {
        try { await synth.GenerateAsync(text, 66, CancellationToken.None); throw new Exception("Unsupported text was silently accepted."); }
        catch (UnsupportedVoiceTextException) { cases.Add(new { text, rejectedBeforeNativeInference = true }); }
    }
    // Cancel an actual long request after it starts, then immediately use the same store again.
    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
    var cancellationWatch = Stopwatch.StartNew(); var cancelledDuringUse = false;
    try { await synth.GenerateAsync(string.Concat(Enumerable.Repeat("我们一起学习汉语。", 30)), 66, cancel.Token); }
    catch (OperationCanceledException) { cancelledDuringUse = true; }
    if (!cancelledDuringUse) throw new Exception("Long synthesis did not observe cancellation.");
    var cancellationMs = cancellationWatch.Elapsed.TotalMilliseconds;
    Inspect(await synth.GenerateAsync("取消后继续朗读。", 66, CancellationToken.None));
    // Exercise disposal and cancellation repeatedly, without a persistent native engine cache.
    for (var i = 0; i < 30; i++)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await synth.GenerateAsync(string.Concat(Enumerable.Repeat("学习汉语。", 20)), i, stop.Token); }
        catch (OperationCanceledException) { }
    }
    Inspect(await synth.GenerateAsync("连续取消后仍能朗读。", 66, CancellationToken.None));
    process.Refresh(); timings.Sort();
    var result = new { status = "PASS", voices = audit, corpus = cases, cancellationMs, repeatedCancellation = 30,
        generationMs = new { p50 = timings[timings.Count / 2], p95 = timings[(int)Math.Ceiling(timings.Count * .95) - 1], max = timings[^1] },
        memory = new { initialPrivateBytes = initialMemory, finalPrivateBytes = process.PrivateMemorySize64, peakWorkingSetBytes = process.PeakWorkingSet64 },
        environment = new { os = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription },
        scope = "Native Windows CPU synthesis and WAV signal checks; not listening review or device playback latency." };
    var path = Path.Combine(root, "audit.json");
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS {audit.Count} voices, {cases.Count} corpus cases, 30 cancellation cycles. Report: {path}");
    return;
}
var results = new List<object>();
foreach (var speaker in new[] { 66, 10 })
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var wav = await synth.GenerateAsync("这是我自己输入的文字。今天是2026年9月19日，我们一起学习汉语。", speaker, CancellationToken.None);
    var path = Path.Combine(root, $"voice-{speaker}.wav"); await File.WriteAllBytesAsync(path, wav);
    results.Add(new { speaker, bytes = wav.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(wav)), elapsedSeconds = timer.Elapsed.TotalSeconds, path });
}
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await synth.GenerateAsync("取消的文字", 66, cancelled.Token); throw new Exception("Cancellation was ignored"); }
catch (OperationCanceledException) { }
var report = JsonSerializer.Serialize(new { status = "PASS", pack = store.Pack.Id, installed = await store.StateAsync(), realInference = results, preCancellation = "PASS" }, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(root, "report.json"), report); Console.WriteLine(report);

static object Inspect(byte[] wav)
{
    if (wav.Length < 46 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || BitConverter.ToInt32(wav, 40) != wav.Length - 44)
        throw new InvalidDataException("Invalid generated WAV.");
    var sampleRate = BitConverter.ToInt32(wav, 24); var count = (wav.Length - 44) / 2;
    double power = 0; var nonzero = 0; var clipped = 0;
    for (var i = 44; i < wav.Length; i += 2)
    {
        var sample = BitConverter.ToInt16(wav, i); power += (double)sample * sample;
        if (sample != 0) nonzero++;
        if (Math.Abs((int)sample) >= 32760) clipped++;
    }
    var rms = Math.Sqrt(power / count) / 32768;
    if (sampleRate != 8000 || rms < .0001 || nonzero < count / 100) throw new InvalidDataException("Silent or invalid voice.");
    return new { bytes = wav.Length, sampleRate, seconds = (double)count / sampleRate, rms, clippedRatio = (double)clipped / count };
}
