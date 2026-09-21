using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;

internal static class D3ReadingProbe
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "HanMate-d3-reading-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = new HanMateDatabase(Path.Combine(root, "hanmate.db"));
        const string sentence = "我们一起学习汉语，今天去银行。";
        const string ending = "长文到这里结束。";
        var longText = string.Concat(Enumerable.Repeat(sentence, 1400))[..(20000 - ending.Length)] + ending;
        const string mixed = "你好，世界！ Hello 3.14 / n\u030C / e\u0301 / 👩‍💻 / 👨‍👩‍👧‍👦\r\n\r\n「学习（中文）」。https://example.invalid/abcdefghijklmnopqrstuvwxyz0123456789\n";
        var cases = new[] {
            (Title: "D3-Long-20000", Text: longText, Kind: ContentKind.Text),
            (Title: "D3-Mixed-Unicode", Text: string.Concat(Enumerable.Repeat(mixed, 8)) + "混排到这里结束。", Kind: ContentKind.Text),
            (Title: "D3-Poem-Lines", Text: string.Concat(Enumerable.Repeat("今天一起读。\r\n明天继续学。\r\n\r\n", 20)) + "最后一行。\n", Kind: ContentKind.Poem)
        };
        var results = new List<object>();
        foreach (var item in cases)
        {
            var draft = new TextDraft(item.Title, item.Text, item.Kind, "textDraft.v2");
            var doc = DraftAnnotation.Generate(draft, BundledAnnotationLexicon.Default.Engine).Document;
            doc = (await new EditorCommitStore(database).CommitAsync(await new TextDraftStore(database).SaveAsync(
                Guid.NewGuid(), draft with { Annotation = doc }, 0))).Document;
            var reading = new ReadingDocument(doc);
            var atoms = reading.Pages.SelectMany(p => p.Parts).SelectMany(p => p.Atoms).ToArray();
            if (string.Concat(atoms.Select(a => a.Text)) != item.Text || reading.Pages.Any(p => p.Parts.Sum(x => x.Atoms.Count) > ReadingDocument.PageAtomLimit))
                throw new Exception("Reading projection lost content or exceeded the page bound.");
            foreach (var target in reading.Targets)
                if (string.Concat(reading.PagesFor(target).SelectMany(p => p.Parts).SelectMany(p => p.Atoms).Select(a => a.Text)) != reading.Segment(target)!.Text)
                    throw new Exception("Selected segment does not match saved text.");
            results.Add(new { item.Title, doc.Id, utf16 = item.Text.Length, atoms = atoms.Length, pages = reading.Pages.Count,
                targets = reading.Targets.Count, firstUnit = doc.TextUnits[0].Id, firstSegment = reading.Targets[0].SegmentId,
                textSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(item.Text))), status = "PASS" });
        }
        var exporter = new BackupExportStore(database);
        await using (var output = File.Create(Path.Combine(root, "d3-reading.hanbackup")))
            await exporter.ExportAsync(await exporter.PreviewAsync(), output, false);
        await File.WriteAllTextAsync(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { root, results }));
    }
}
