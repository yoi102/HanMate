using System.Diagnostics;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Dictionary;

// Disposable database/cache only. Never touches application data.
if (args.Length != 0 && (args.Length != 2 || args[0] != "--root"))
    throw new ArgumentException("Usage: HanMate.DictionaryProbe [--root <disposable-directory>]");
var root = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "HanMate-dictionary-probe");
Directory.CreateDirectory(root);
Console.WriteLine($"root={root} pid={Environment.ProcessId}");
var store = new OfflineSearchStore(new HanMateDatabase(Path.Combine(root, "probe.db")), new DefaultDictionaryStore(root));
var watch = Stopwatch.StartNew();
await store.PrepareAsync();
Console.WriteLine($"prepare_ms={watch.Elapsed.TotalMilliseconds:F1}");
foreach (var word in new[] { "八", "生", "金", "学习" })
{
    for (var run = 0; run < 3; run++)
    {
        watch.Restart();
        var page = await store.SearchAsync(word);
        var searchMs = watch.Elapsed.TotalMilliseconds;
        var document = page.Items.Where(i => i.Headword == word)
            .Select(i => JsonSerializer.Deserialize<ContentDocument>(i.BodyJson, ContentJson.Options)!).First();
        watch.Restart();
        var lookup = await store.FindEntryAsync(document.TextUnits.Single(u => u.Role == TextUnitRole.Headword));
        var exactMs = watch.Elapsed.TotalMilliseconds;
        if (lookup.Document?.Id != document.Id || !lookup.IsReadOnly) throw new InvalidOperationException("Exact lookup differs.");
        watch.Restart();
        var detail = DictionaryDetailProjection.Create(document);
        Console.WriteLine($"{word} run={run} search_ms={searchMs:F1} exact_ms={exactMs:F1} projection_ms={watch.Elapsed.TotalMilliseconds:F1} results={page.Items.Count} definition_atoms={detail.Definitions.Sum(d => d.Atoms.Count)}");
    }
}
