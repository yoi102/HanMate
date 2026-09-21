using HanMate.Core.Search;
using System.Text;

namespace HanMate.Core.Tests;

public sealed class HandwritingRecognizerTests
{
    private static readonly Lazy<HandwritingRecognizer> Model = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures/Handwriting/medians.jsonl.gz"));
        return HandwritingRecognizer.Load(file);
    });
    public static TheoryData<string, InkPoint[][]> HandDrawn => new()
    {
        { "一", [[new(20, 50), new(85, 48)]] },
        { "十", [[new(20, 40), new(85, 40)], [new(50, 10), new(50, 90)]] },
        { "人", [[new(55, 15), new(50, 48), new(20, 85)], [new(51, 42), new(65, 67), new(85, 88)]] },
        { "口", [[new(20, 20), new(20, 80)], [new(20, 20), new(80, 20), new(80, 80)], [new(20, 80), new(80, 80)]] },
        { "大", [[new(20, 40), new(85, 40)], [new(52, 10), new(50, 48), new(38, 73), new(15, 90)], [new(52, 43), new(66, 67), new(88, 90)]] },
        { "中", [[new(20, 30), new(20, 65)], [new(20, 30), new(80, 30), new(80, 65)], [new(20, 65), new(80, 65)], [new(50, 10), new(50, 90)]] }
    };
    [Theory, MemberData(nameof(HandDrawn))]
    public void IndependentDrawingsRemainCandidatesAfterTranslationAndScale(string character, InkPoint[][] strokes)
    {
        Assert.Contains(Model.Value.Recognize(strokes, 6), c => c.Character == character);
        var transformed = strokes.Select(s => s.Select(p => new InkPoint(143 + p.X * 2.8f, 270 + p.Y * 2.8f)).ToArray()).ToArray();
        Assert.Equal(Model.Value.Recognize(strokes).Select(c => c.Character), Model.Value.Recognize(transformed).Select(c => c.Character));
    }
    [Fact]
    public void PrefixRecognitionAndUndoUseCurrentStrokeSnapshot()
    {
        InkPoint[][] horizontal = [[new(10, 40), new(90, 40)]];
        InkPoint[][] cross = [horizontal[0], [new(50, 10), new(50, 90)]];
        Assert.Equal("一", Model.Value.Recognize(horizontal)[0].Character);
        Assert.Equal("十", Model.Value.Recognize(cross)[0].Character);
        Assert.Equal("一", Model.Value.Recognize(cross.Take(1).ToArray())[0].Character);
        Assert.Empty(Model.Value.Recognize([]));
        Assert.Equal(9574, Model.Value.CharacterCount);
    }
    [Fact]
    public void EqualDistancesKeepOrdinalOrderAndPreferCompletedCharacters()
    {
        const string templates = """
            {"format":1}
            {"character":"七","strokes":[[[0,0],[10,0]]]}
            {"character":"丁","strokes":[[[0,0],[10,0]]]}
            {"character":"一","strokes":[[[0,0],[10,0]]]}
            {"character":"十","strokes":[[[0,0],[10,0]],[[5,-5],[5,5]]]}
            """;
        using var bytes = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(bytes, System.IO.Compression.CompressionMode.Compress, true))
            gzip.Write(Encoding.UTF8.GetBytes(templates));
        bytes.Position = 0; var model = HandwritingRecognizer.Load(bytes);
        InkPoint[][] input = [[new(0,0),new(10,0)]];
        Assert.Equal(new[] { "一", "丁" }, model.Recognize(input, 2).Select(c => c.Character));
        Assert.Equal(new[] { "一", "丁", "七", "十" }, model.Recognize(input, 12).Select(c => c.Character));
        Assert.Equal("十", model.Recognize([input[0], [new(5,-5),new(5,5)]], 1)[0].Character);
    }
    [Fact]
    public void PreparedAssetPreservesRecognitionAndRejectsTruncation()
    {
        using var bytes = new MemoryStream(); Model.Value.WritePrepared(bytes); bytes.Position = 0;
        Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/Handwriting/templates.bin")), bytes.ToArray());
        var prepared = HandwritingRecognizer.LoadPrepared(bytes);
        foreach (var drawing in HandDrawn)
        {
            var strokes = (InkPoint[][])drawing[1];
            Assert.Equal(Model.Value.Recognize(strokes), prepared.Recognize(strokes));
        }
        var content = bytes.ToArray();
        using var truncated = new MemoryStream(content[..^10]);
        Assert.Throws<EndOfStreamException>(() => HandwritingRecognizer.LoadPrepared(truncated));
    }
    [Theory, MemberData(nameof(HandDrawn))]
    public void NonstandardStrokeOrderAndDirectionKeepIntendedCharacter(string character, InkPoint[][] strokes)
    {
        var reversed = strokes.Reverse().Select(s => s.Reverse().ToArray()).ToArray();
        Assert.Contains(Model.Value.Recognize(reversed, 6), c => c.Character == character);
        var distorted = reversed.Select(s => s.Select(p => new InkPoint(p.X * 1.15f, p.Y * .9f)).ToArray()).ToArray();
        Assert.Contains(Model.Value.Recognize(distorted, 6), c => c.Character == character);
    }
    [Fact]
    public void ConnectedZiKeepsChildCharacterWithOneOrTwoPenTraces()
    {
        InkPoint[] first = [new(30,20),new(70,18),new(48,42)];
        InkPoint[] second = [new(47,40),new(52,50),new(54,80),new(51,91),new(37,83)];
        InkPoint[] horizontal = [new(12,57),new(88,53)];
        InkPoint[][] two = [first.Concat(second).ToArray(),horizontal];
        InkPoint[][] one = [first.Concat(second).Concat(horizontal).ToArray()];
        Assert.Contains(Model.Value.Recognize(two, 6), c => c.Character == "子");
        Assert.Contains(Model.Value.Recognize(one, 6), c => c.Character == "子");
        Assert.Contains(Model.Value.Recognize(one.Select(s => s.Select(p => new InkPoint(110 + p.X * 1.1f, 50 + p.Y * .95f)).ToArray()).ToArray(), 6), c => c.Character == "子");
    }
    [Fact]
    public void ConnectedShengAndJinSupportSeveralMissingPenLifts()
    {
        InkPoint[][] sheng = [[new(33,13),new(29,32),new(18,45)], [new(28,31),new(79,29)],
            [new(27,57),new(76,55)], [new(50,8),new(50,88)], [new(12,87),new(90,85)]];
        InkPoint[][] jin = [[new(50,10),new(38,30),new(16,48)], [new(50,10),new(67,33),new(87,45)],
            [new(33,40),new(67,40)], [new(26,56),new(75,55)], [new(50,41),new(50,88)],
            [new(28,67),new(35,79)], [new(72,66),new(64,79)], [new(15,90),new(87,90)]];
        Check("生", sheng); Check("金", jin);
        Check("生", [Join(sheng,0,3),Join(sheng,3,2)]);
        Check("金", [Join(jin,0,2),Join(jin,2,3),Join(jin,5,3)]);
        Check("金", [jin[0],jin[1],Join(jin,2,3),Join(jin,5,2),jin[7]]);
        void Check(string character, InkPoint[][] strokes)
        {
            var result = Model.Value.Recognize(strokes, 6);
            Assert.True(result.Any(c => c.Character == character), $"{character} with {strokes.Length} pen traces: {string.Join(',',result.Select(c=>c.Character))}");
        }
        static InkPoint[] Join(InkPoint[][] strokes,int start,int count) => strokes.Skip(start).Take(count).SelectMany(s=>s).ToArray();
    }
    [Fact]
    public void BoxWithCornerLiftRemainsACandidate()
    {
        InkPoint[][] strokes = [[new(20,20),new(20,80)], [new(20,20),new(80,20)],
            [new(80,20),new(80,80)], [new(20,80),new(80,80)]];
        Assert.Contains(Model.Value.Recognize(strokes, 6), c => c.Character == "口");
    }
    [Fact]
    public void WarmRecognitionDoesNotAllocateAPointArrayPerTemplate()
    {
        var model = Model.Value; InkPoint[][] input = [[new(20,50),new(85,48)]];
        model.Recognize(input, 6);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = model.Recognize(input, 6);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal("一", result[0].Character);
        Assert.True(allocated < 100_000, $"Warm recognition allocated {allocated} bytes.");
    }
    [Fact]
    public void InvalidInputAndCancellationAreBounded()
    {
        Assert.Throws<ArgumentException>(() => Model.Value.Recognize([[new(float.NaN, 0)]]));
        Assert.Throws<ArgumentException>(() => Model.Value.Recognize(Enumerable.Range(0, 65).Select(_ => new[] { new InkPoint(0, 0) }).ToArray()));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Model.Value.Recognize([[new(0, 0)]], cancellationToken: cts.Token));
        Assert.All(Model.Value.Recognize([[new(0, 0)]]), c => Assert.Single(c.Character.EnumerateRunes()));
    }
}
