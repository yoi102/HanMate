using System.Diagnostics;
using System.Security.Cryptography;
using HanMate.Core.Search;

if (args.Length != 2) throw new ArgumentException("Usage: HanMate.HandwritingCompiler <medians.jsonl.gz> <templates.bin>");
using (var source = File.OpenRead(args[0]))
using (var output = File.Create(args[1])) HandwritingRecognizer.Load(source).WritePrepared(output);
using (var compiled = new BufferedStream(File.OpenRead(args[1]), 65536))
    Console.WriteLine($"Prepared characters={HandwritingRecognizer.LoadPrepared(compiled).CharacterCount}");
using (var output = File.OpenRead(args[1])) Console.WriteLine($"bytes={output.Length} sha256={Convert.ToHexStringLower(SHA256.HashData(output))}");
for (var iteration = 0; iteration < 3; iteration++)
{
    var before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
    using (var source = File.OpenRead(args[0])) HandwritingRecognizer.Load(source);
    Console.WriteLine($"JSON load ms={timer.Elapsed.TotalMilliseconds:F1} allocated={GC.GetAllocatedBytesForCurrentThread() - before}");
    before = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
    using var compiled = new BufferedStream(File.OpenRead(args[1]), 65536);
    var model = HandwritingRecognizer.LoadPrepared(compiled);
    Console.WriteLine($"Prepared load ms={timer.Elapsed.TotalMilliseconds:F1} allocated={GC.GetAllocatedBytesForCurrentThread() - before}");
    InkPoint[][] input = [[new(50,10),new(50,90)], [new(20,40),new(85,40)]];
    timer.Restart(); var candidates = model.Recognize(input, 6);
    Console.WriteLine($"Reversed-order cross ms={timer.Elapsed.TotalMilliseconds:F2} candidates={string.Join(',', candidates.Select(c => c.Character))}");
}
