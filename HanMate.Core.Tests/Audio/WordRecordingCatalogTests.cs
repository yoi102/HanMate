using System.Security.Cryptography;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Tests.Audio;

public sealed class WordRecordingCatalogTests
{
    private static WordRecordingCatalog Load() => WordRecordingCatalog.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "WordAudio", "catalog.json")));
    private static PinyinSyllable?[] Readings(string value) => value.Split(' ').Select(p => PinyinSyllableParser.ParseDictionarySyllable(p)).ToArray();

    [Theory]
    [InlineData("趴", "pa1")]
    [InlineData("怕", "pa4")]
    [InlineData("吃", "chi1")]
    [InlineData("赤", "chi4")]
    [InlineData("行", "xing2")]
    [InlineData("行", "hang2")]
    [InlineData("女", "nv3")]
    [InlineData("雌", "ci1")]
    public void SingleCharacterUsesExactNumberedTone(string text, string reading)
    {
        var asset = Load().Find(text, Readings(reading));
        Assert.NotNull(asset);
        Assert.Equal(Readings(reading)[0]!.Base + Readings(reading)[0]!.Tone, asset.Pinyin);
    }

    [Theory]
    [InlineData("银行", "yin2 hang2")]
    [InlineData("音乐", "yin1 yue4")]
    [InlineData("爸爸", "ba4 ba0")]
    [InlineData("苹果", "ping2 guo3")]
    public void WholeWordRequiresTextAndEveryReading(string text, string reading)
    {
        var catalog = Load();
        var readings = Readings(reading);
        var asset = catalog.Find(text, readings);
        Assert.NotNull(asset); Assert.Equal(text, asset.Text);
        var changed = readings.ToArray(); changed[^1] = changed[^1]! with { Tone = (changed[^1]!.Tone + 1) % 5 };
        Assert.Null(catalog.Find(text, changed));
        Assert.Null(catalog.Find(text + text, readings));
    }

    [Theory]
    [InlineData("银行", "yin2 xing2")]
    [InlineData("爸爸", "ba4 ba4")]
    [InlineData("趴趴趴趴", "pa1 pa1 pa1 pa1")]
    [InlineData("花儿", "huar1")]
    [InlineData("a", "a1")]
    [InlineData("你。", "ni3")]
    public void UnmatchedOrIncompleteReadingFallsBackWithoutSyllableConcatenation(string text, string reading) =>
        Assert.Null(Load().Find(text, Readings(reading)));

    [Fact]
    public void MissingAnnotationAndNonHeadwordUnitsAreNotEligible()
    {
        var catalog = Load();
        Assert.Null(catalog.Find("行", [null]));
        var course = PinyinCourse.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-course.json")));
        var headword = course.Data.Contents.SelectMany(c => c.TextUnits).First(u => u.Role == TextUnitRole.Headword && u.Text == "苹果");
        Assert.NotNull(catalog.Find(headword));
        Assert.Null(catalog.Find(headword with { Role = TextUnitRole.Definition }));
        Assert.Null(catalog.Find(headword with { Tokens = headword.Tokens.Reverse().ToArray() }));
    }

    [Fact]
    public void BundledFilesMatchHashesAndKeepUnreviewedProvenance()
    {
        var catalog = Load();
        Assert.True(catalog.Data.Assets.Count(a => a.Text is null) > 1000);
        Assert.True(catalog.Data.Assets.Count(a => a.Text?.Length > 1) > 6000);
        foreach (var asset in catalog.Data.Assets.DistinctBy(a => a.Key))
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", asset.File);
            using var stream = File.OpenRead(path);
            Assert.Equal(asset.Sha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
            if (path.EndsWith(".mp3", StringComparison.Ordinal))
            {
                stream.Position = 0;
                Assert.Equal("mp3", CompressedAudio.Inspect(stream).Container);
            }
            Assert.Equal("needsReview", asset.ReviewStatus);
            Assert.Contains("ff9ed3d0c631195bd2c06f39450f3264c7124040", asset.SourceUrl);
        }
        // Distinct readings must never be attached to the very same whole-word recording.
        foreach (var group in catalog.Data.Assets.Where(a => a.Text?.Length > 1).GroupBy(a => a.SourceUrl))
            Assert.Single(group.Select(a => a.Pinyin).Distinct());
    }

    [Fact]
    public void InvalidPathsAndConflictingBindingsAreRejected()
    {
        var asset = Load().Data.Assets.First();
        Assert.Throws<InvalidDataException>(() => new WordRecordingCatalog(new("test", [asset with { File = "../outside.mp3" }])));
        Assert.Throws<InvalidDataException>(() => new WordRecordingCatalog(new("test", [asset, asset])));
    }
}
