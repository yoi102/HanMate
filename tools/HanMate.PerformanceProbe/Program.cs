using System.Diagnostics;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Pinyin;

// All data lives in a new disposable TEMP directory. Never open the user's application database.
var root = Path.Combine(Path.GetTempPath(), "HanMate-performance", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(root);
var results = new List<object>();
var timer = Stopwatch.StartNew(); var engine = BundledAnnotationLexicon.Default.Engine;
var coldLexiconMs = timer.Elapsed.TotalMilliseconds;
foreach (var length in new[] { 1000, 5000, 20000 })
{
    const string sentence = "我们一起学习汉语，今天去银行。";
    var text = string.Concat(Enumerable.Repeat(sentence, (length + sentence.Length - 1) / sentence.Length))[..length];
    var draft = new TextDraft("Performance fixture", text);
    var doc = DraftAnnotation.Generate(draft, engine).Document;
    results.Add(await Measure($"annotation-{length}", () => { doc = DraftAnnotation.Generate(draft, engine).Document; return Task.CompletedTask; }));
    results.Add(await Measure($"reading-projection-{length}", () =>
    {
        var reading = new ReadingDocument(doc);
        if (reading.Pages.Any(p => p.Parts.Sum(part => part.Atoms.Count) > ReadingDocument.PageAtomLimit)
            || string.Concat(reading.Pages.SelectMany(p => p.Parts).SelectMany(p => p.Atoms).Select(a => a.Text)) != text)
            throw new InvalidOperationException("Reading content loss or unbounded page.");
        return Task.CompletedTask;
    }));
}
foreach (var size in new[] { 10000, 100000 })
{
    var db = new HanMateDatabase(Path.Combine(root, $"search-{size}.db"));
    var word = DraftAnnotation.Generate(new TextDraft("中国", "中国", ContentKind.Word), engine).Document;
    word = word with { TextUnits = word.TextUnits.Select(u => u with { Tokens = u.Tokens.Select(t => t with { ReviewState = AnnotationReviewState.Confirmed }).ToArray() }).ToArray() };
    await new SqliteContentDocumentStore(db, new()).SaveAsync(word, 0);
    using (var c = await db.OpenConnectionAsync())
    {
        using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        // Deliberately duplicate a headword to force ranking of the entire corpus. This is a query-only fixture.
        cmd.CommandText = """
            WITH RECURSIVE numbers(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM numbers WHERE n<$count)
            INSERT INTO content SELECT printf('00000000-0000-4000-8000-%012d',n),kind,origin,title,
                json_set(body_json,'$.id',printf('00000000-0000-4000-8000-%012d',n)),row_revision,membership_revision,
                content_revision,annotation_revision,metadata_revision,semantic_fingerprint,created_at_utc,updated_at_utc
            FROM numbers,content WHERE id=$id;
            INSERT INTO search_index SELECT c.id,0,'中国','zhongguo','zhong guo','zhong1guo2','zhong1 guo2',
                '[{"base":"zhong","tone":1},{"base":"guo","tone":2}]',NULL,1
            FROM content c WHERE c.id LIKE '00000000-0000-4000-8000-%';
            UPDATE app_state SET data_epoch=data_epoch+1;
            """;
        cmd.Parameters.AddWithValue("$count", size - 1); cmd.Parameters.AddWithValue("$id", word.Id.ToString("D"));
        await cmd.ExecuteNonQueryAsync(); tx.Commit();
    }
    var search = new OfflineSearchStore(db);
    foreach (var query in new[] { "中国", "国", "zhongguo", "zhong1guo2", "不存在" })
    {
        var expected = query == "不存在" ? 0 : size;
        var first = await search.SearchAsync(query);
        if (first.Total != expected) throw new InvalidOperationException("Search corpus mismatch.");
        results.Add(await Measure($"search-{size}-{query}", async () =>
        {
            var page = await search.SearchAsync(query);
            if (page.Total != expected || page.Items.Count != Math.Min(50, expected)) throw new InvalidOperationException("Search result mismatch.");
        }));
    }
}
var process = Process.GetCurrentProcess();
var report = new { status = "PASS", coldLexiconMs, samplesPerCase = 30, results,
    environment = new { os = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription },
    peakWorkingSetBytes = process.PeakWorkingSet64,
    scope = "Windows production Core/SQLite CPU timings, one warmup per case. Synthetic duplicate-headword corpus; not device UI, frame rate, playback latency, or teaching content." };
var reportPath = Path.Combine(root, "report.json");
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS. Report: {reportPath}");

static async Task<object> Measure(string name, Func<Task> action)
{
    await action(); var samples = new double[30];
    for (var i = 0; i < samples.Length; i++)
    { var timer = Stopwatch.StartNew(); await action(); samples[i] = timer.Elapsed.TotalMilliseconds; }
    var sorted = samples.Order().ToArray();
    var result = new { name, p50Ms = sorted[15], p95Ms = sorted[28], maxMs = sorted[^1], samplesMs = samples };
    Console.WriteLine($"{name}: P50={result.p50Ms:F1} P95={result.p95Ms:F1} max={result.maxMs:F1} ms");
    return result;
}
