using System.Security.Cryptography;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Tests.Pinyin;

public sealed class PinyinCourseTests
{
    [Fact]
    public void DefinitionReadingsCoverEveryHanziWithoutPretendingToBeReviewed()
    {
        var course = Load();
        var definitions = course.Data.Contents.SelectMany(d => d.TextUnits)
            .Where(u => u.Role == HanMate.Core.Content.TextUnitRole.Definition).ToArray();
        Assert.Equal(418, definitions.Length);
        foreach (var unit in definitions)
        {
            Assert.Equal(unit.Text, string.Concat(unit.Tokens.Select(t => t.Text)));
            Assert.All(unit.Tokens.Where(t => t.Kind == HanMate.Core.Content.TokenKind.Hanzi), t =>
            {
                Assert.NotNull(t.Pinyin); Assert.True(t.Locked);
                Assert.Equal(HanMate.Core.Content.AnnotationReviewState.NeedsReview, t.ReviewState);
                Assert.Equal(HanMate.Core.Content.PinyinFormatter.Format(t.Pinyin.Base, t.Pinyin.Tone, false), t.Pinyin.Display);
            });
            Assert.All(unit.Tokens.Where(t => t.Kind != HanMate.Core.Content.TokenKind.Hanzi), t => Assert.Null(t.Pinyin));
        }
        Assert.All(course.Data.Contents, d => Assert.Equal(HanMate.Core.Content.ReviewStatus.Draft, d.Source.ReviewStatus));
    }

    [Theory]
    [InlineData("因过错而受到惩处。", "yin1 guo4 cuo4 er2 shou4 dao4 cheng2 chu3")]
    [InlineData("量长度的工具；长度单位。", "liang2 chang2 du4 de0 gong1 ju4 chang2 du4 dan1 wei4")]
    [InlineData("从云中降落的水滴。", "cong2 yun2 zhong1 jiang4 luo4 de0 shui3 di1")]
    [InlineData("用于恶心，表示想呕吐或令人厌恶。", "yong4 yu2 e3 xin0 biao3 shi4 xiang3 ou3 tu4 huo4 ling4 ren2 yan4 wu4")]
    [InlineData("教学生知识和技能的人。", "jiao1 xue2 sheng0 zhi1 shi0 he2 ji4 neng2 de0 ren2")]
    public void DefinitionPolyphonesFollowTheirSpecificContext(string text, string readings)
    {
        var unit = Load().Data.Contents.SelectMany(d => d.TextUnits).Single(u => u.Text == text);
        Assert.Equal(readings, string.Join(" ", unit.Tokens.Where(t => t.Pinyin is not null).Select(t => t.Pinyin!.Base + t.Pinyin.Tone)));
    }

    [Fact]
    public void ItemClickSelectsAnExplicitAvailableExampleAndNeverInventsMissingAudio()
    {
        var course = Load(); var b = course.Data.Items.Single(i => i.Group == "initial" && i.Display == "b");
        var example = course.ClickExample(b)!;
        Assert.Equal("ba1", example.AudioKey); Assert.Equal("八", course.Unit(example).Text);
        Assert.Equal("ma1", course.ClickExample(course.Data.Items.Single(i => i.Group == "initial" && i.Display == "m"))!.AudioKey);
        var noAudio = new PinyinCourse(course.Data with { Items = [b with { DemoAudioKey = null, Examples = [example] }], Assets = [] });
        Assert.Null(noAudio.ClickExample(noAudio.Data.Items[0]));
        Assert.Throws<ArgumentException>(() => course.ClickExample(b with { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void SyllableRecordingCannotMasqueradeAsLongerExampleWord()
    {
        var course = Load(); var b = course.Data.Items.Single(i => i.Group == "initial" && i.Display == "b"); var example = b.Examples[0];
        var document = course.Data.Contents.Single(c => c.Id == example.ContentId); var unit = course.Unit(example); var token = unit.Tokens.Single();
        var second = token with { Id = Guid.NewGuid(), Start = 1 };
        var longer = unit with { Text = token.Text + token.Text, ElementBoundariesUtf16 = [0, 1, 2], Tokens = [token, second],
            Segments = [unit.Segments[0] with { Text = token.Text + token.Text, Length = 2, TokenIds = [token.Id, second.Id] }] };
        var modified = new PinyinCourse(course.Data with { Contents = course.Data.Contents.Select(c => c.Id == document.Id ? c with { TextUnits = [longer] } : c).ToArray() });
        Assert.Null(modified.Audio(example)); Assert.NotEqual(example, modified.ClickExample(b));
    }
    private static PinyinCourse Load() => PinyinCourse.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-course.json")));
    [Fact]
    public void CInitialUsesCongWithoutChangingTheWholeCiLessonReading()
    {
        var course = Load();
        var initial = course.Data.Items.Single(i => i.Group == "initial" && i.Display == "c");
        Assert.DoesNotContain(initial.Examples, e => course.Unit(e).Text.Contains('雌'));
        var character = Assert.Single(initial.Examples, e => course.Unit(e).Text == "匆");
        Assert.Equal(1, character.Tone);
        Assert.Equal("cong1", course.PlaybackKey(character));
        Assert.EndsWith("/cmn-cong1.mp3", course.Audio(character)!.SourceUrl);
        var word = Assert.Single(initial.Examples, e => course.Unit(e).Text == "匆忙");
        Assert.Equal("cong1 mang2", course.Audio(word)!.Pinyin);
        var whole = course.Data.Items.Single(i => i.Group == "whole" && i.Display == "ci");
        Assert.DoesNotContain(whole.Examples, e => e.AudioKey.StartsWith("cong"));
        Assert.Contains(whole.Examples, e => course.Unit(e).Text == "雌" && e.AudioKey == "ci1" && e.Tone == 1);
    }
    private static IReadOnlyDictionary<string, string> PairingDrafts() => File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pinyin-character-words.tsv"))
        .Where(line => line.Length > 0 && !line.StartsWith('#')).Select(line => line.Split(';'))
        .ToDictionary(fields => fields[0], fields => fields[1], StringComparer.Ordinal);

    private static void AssertBundledAudioAvailability(PinyinCourse course, PinyinExample example, IReadOnlyDictionary<string, string> drafts)
    {
        var unit = course.Unit(example);
        if (drafts.TryGetValue(unit.Text, out var readings))
        {
            Assert.Equal(readings, string.Join(" ", unit.Tokens.Select(t => t.Pinyin!.Base + t.Pinyin.Tone)));
            Assert.Null(example.WordAudioKey);
            Assert.Null(course.Audio(example));
            Assert.Null(course.PlaybackKey(example));
        }
        else
        {
            // All recordings available before the 42 explicitly unrecorded drafts remain required.
            Assert.NotNull(course.Audio(example));
            Assert.NotNull(course.PlaybackKey(example));
        }
    }

    [Fact]
    public void OnlyTheExplicitPairingDraftsLackCompleteWordRecordings()
    {
        var course = Load(); var drafts = PairingDrafts();
        Assert.Equal(42, drafts.Count);
        var examples = course.Data.Items.SelectMany(i => i.Examples).ToArray();
        var missing = examples.Where(e => course.PlaybackKey(e) is null).Select(e => course.Unit(e).Text).ToHashSet(StringComparer.Ordinal);
        Assert.True(missing.SetEquals(drafts.Keys));
        Assert.All(examples, e => AssertBundledAudioAvailability(course, e, drafts));
        var example = examples.First(e => course.Unit(e).Text == "山坡");
        var imported = new PinyinCourse(course.Data, new Dictionary<Guid, string> { [example.UnitId] = "local:checked:pairing-fixture" });
        Assert.Equal("local:checked:pairing-fixture", imported.PlaybackKey(example));
    }

    [Theory]
    [InlineData("p", 1, "坡", "山坡")]
    [InlineData("p", 1, "趴", "趴下")]
    [InlineData("m", 4, "骂", "责骂")]
    [InlineData("h", 3, "火", "火车")]
    [InlineData("q", 2, "骑", "骑马")]
    [InlineData("c", 1, "匆", "匆忙")]
    public void MatchingWordsFollowTheirCharacterAndHighlightTheSameReading(string display, int tone, string character, string word)
    {
        var course = Load(); var item = course.Data.Items.Single(i => i.Group == "initial" && i.Display == display);
        var ordered = course.ExamplesForTone(item, tone).ToList();
        var index = ordered.FindIndex(e => course.Unit(e).Text == word);
        Assert.True(index > 0);
        var precedingCharacter = ordered.Take(index).Last(e => course.Unit(e).Tokens.Count == 1);
        Assert.Equal(character, course.Unit(precedingCharacter).Text);
        var target = course.Unit(ordered[index]).Tokens.Single(t => t.Id == ordered[index].TokenId);
        var original = course.Unit(precedingCharacter).Tokens[0];
        Assert.Equal((original.Text, original.Pinyin!.Base, original.Pinyin.Tone), (target.Text, target.Pinyin!.Base, target.Pinyin.Tone));
        Assert.Equal(item.Examples.Count(e => e.Tone == tone), ordered.Count);
        Assert.Equal(ordered.Count, ordered.Distinct().Count());
        Assert.True(item.Examples.Where(e => e.Tone == tone).ToHashSet().SetEquals(ordered));
    }
    [Theory]
    [InlineData("initial", "p")]
    [InlineData("initial", "ch")]
    [InlineData("whole", "chi")]
    public void ReportedLessonsHaveCharactersAndWholeWordsInEveryTone(string group, string display)
    {
        var course = Load(); var item = course.Data.Items.Single(i => i.Group == group && i.Display == display);
        for (var tone = 1; tone <= 4; tone++)
        {
            var examples = item.Examples.Where(e => e.Tone == tone).ToArray();
            Assert.Contains(examples, e => course.Unit(e).Tokens.Count == 1);
            Assert.Contains(examples, e => course.Unit(e).Tokens.Count > 1 && e.WordAudioKey is not null);
            Assert.All(examples, e => AssertBundledAudioAvailability(course, e, PairingDrafts()));
        }
    }

    [Fact]
    public void InitialCoverageDoesNotConfuseMissingSyllableTonesWithMissingInitialTones()
    {
        var course = Load();
        foreach (var item in course.Data.Items.Where(i => i.Group is "initial" or "spelling"))
            for (var tone = 1; tone <= 4; tone++)
            {
                // No common standard first-tone r or second-tone z/s examples.
                if ((item.Display == "r" && tone == 1) || (item.Display is "z" or "s" && tone == 2)) continue;
                Assert.Contains(item.Examples, e => e.Tone == tone && course.Unit(e).Tokens.Count == 1);
                Assert.Contains(item.Examples, e => e.Tone == tone && course.Unit(e).Tokens.Count > 1);
            }
    }

    [Fact]
    public void CompleteTableKeepsInitialsSpellingLettersAndWholeSyllablesSeparate()
    {
        var course = Load();
        Assert.Equal(63, course.Data.Items.Count);
        Assert.Equal(new[] { 21, 2, 6, 9, 9, 16 }, course.Data.Items.GroupBy(i => i.Group).Select(g => g.Count()));
        Assert.Equal(418, course.Data.Contents.Count);
        Assert.All(course.Data.Items, item => Assert.All(item.Examples, e => Assert.InRange(e.Tone, 1, 4)));
        var r = course.Data.Items.Single(i => i.Group == "initial" && i.Display == "r");
        Assert.Contains(r.Examples, e => course.Unit(e).Text == "人民");
        Assert.Contains(r.Examples, e => e.Tone == 3 && course.Unit(e).Text == "忍");
    }

    [Fact]
    public void OnlySelectedInitialIsHighlightedAndUmlautSpellingIsExplained()
    {
        var course = Load();
        var m = course.Data.Items.Single(i => i.Group == "initial" && i.Display == "m");
        Assert.All(m.Examples, e => { Assert.Equal(0, e.PinyinStart); Assert.Equal(1, e.PinyinLength); });
        var whole = course.Data.Items.Single(i => i.Group == "whole" && i.Display == "zhi");
        Assert.All(whole.Examples, e => Assert.Equal(3, e.PinyinLength));
        var umlaut = course.Data.Items.Single(i => i.Display == "ü");
        Assert.True(umlaut.SpellingNote); Assert.All(umlaut.Examples, e => Assert.Equal(1, e.PinyinStart));
    }

    [Theory]
    [InlineData("tone")]
    [InlineData("highlight")]
    [InlineData("audio")]
    [InlineData("duplicate")]
    public void RejectsMismatchedTeachingBindings(string mutation)
    {
        var data = Load().Data; var item = data.Items[0]; var example = item.Examples[0];
        var bad = mutation switch
        {
            "tone" => example with { Tone = 4 },
            "highlight" => example with { PinyinStart = 1 },
            "audio" => example with { AudioKey = "ma1" },
            _ => example
        };
        var examples = mutation == "duplicate" ? new[] { example, example } : new[] { bad };
        Assert.Throws<InvalidDataException>(() => new PinyinCourse(data with { Items = new[] { item with { Examples = examples } } }));
    }

    [Fact]
    public void EveryShippedRecordingHasMatchingBytesAndNonSilentPcmData()
    {
        var course = Load(); Assert.NotEmpty(course.Data.Assets);
        foreach (var asset in course.Data.Assets)
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", asset.File));
            Assert.Equal(asset.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            using var reader = new BinaryReader(new MemoryStream(bytes));
            Assert.Equal("RIFF", new string(reader.ReadChars(4))); reader.ReadInt32(); Assert.Equal("WAVE", new string(reader.ReadChars(4)));
            int bytesPerSecond = 0; byte[]? pcm = null;
            while (reader.BaseStream.Position + 8 <= bytes.Length)
            {
                var chunk = new string(reader.ReadChars(4)); var size = reader.ReadInt32(); var next = reader.BaseStream.Position + size + size % 2;
                if (chunk == "fmt ") { Assert.Equal(1, reader.ReadInt16()); Assert.Equal(1, reader.ReadInt16()); reader.ReadInt32(); bytesPerSecond = reader.ReadInt32(); reader.ReadInt16(); Assert.Equal(16, reader.ReadInt16()); }
                if (chunk == "data") pcm = reader.ReadBytes(size);
                reader.BaseStream.Position = next;
            }
            Assert.NotNull(pcm); Assert.Contains(pcm, b => b != 0);
            Assert.InRange(Math.Abs((double)pcm.Length / bytesPerSecond - asset.DurationSeconds), 0, .001);
            Assert.StartsWith("https://github.com/hugolpz/audio-cmn/blob/ff9ed3d0c631195bd2c06f39450f3264c7124040/", asset.SourceUrl);
            Assert.Equal("needsReview", asset.ReviewStatus);
        }
    }

    [Fact]
    public void TeachingInitialsUseExplicitCommonReadingsAndExamplesDeclareAudioAvailabilityAndDefinitions()
    {
        var course = Load();
        var sounds = new[] { "bo1", "po1", "mo1", "fo1", "de1", "te1", "ne1", "le1", "ge1", "ke1", "he1", "ji1", "qi1", "xi1", "zhi1", "chi1", "shi1", "ri1", "zi1", "ci1", "si1" };
        Assert.Equal(sounds, course.Data.Items.Where(i => i.Group == "initial").Select(course.DemoPlaybackKey));
        Assert.Null(course.DemoPlaybackKey(course.Data.Items.Single(i => i.Display == "ong")));
        foreach (var example in course.Data.Items.SelectMany(i => i.Examples))
        {
            AssertBundledAudioAvailability(course, example, PairingDrafts());
            Assert.All(course.Unit(example).Tokens, t => Assert.NotNull(t.Pinyin));
            Assert.Contains(course.Content(example).TextUnits, u => u.Role == HanMate.Core.Content.TextUnitRole.Definition && u.Text.Length > 1);
        }
        var word = course.Data.Items.SelectMany(i => i.Examples).First(e => e.WordAudioKey is not null);
        Assert.Equal(course.Unit(word).Text, course.Audio(word)!.Text);
        // A changed non-highlighted syllable cannot keep using the complete-word recording.
        var other = course.Data.Items.SelectMany(i => i.Examples).First(e => e.WordAudioKey is not null && course.Unit(e).Tokens.Count > 1);
        var otherDoc = course.Content(other); var otherUnit = course.Unit(other);
        var last = otherUnit.Tokens[^1]; var altered = last with { Pinyin = last.Pinyin! with { Tone = 3, Display = HanMate.Core.Content.PinyinFormatter.Format(last.Pinyin.Base, 3, false) } };
        // The same word can now teach its second syllable on another page. Isolate
        // this binding so the mutation still tests a non-highlighted syllable.
        var owner = course.Data.Items.First(i => i.Examples.Contains(other));
        var modified = new PinyinCourse(course.Data with { Items = [owner with { Examples = [other] }], Contents = course.Data.Contents.Select(d => d.Id == otherDoc.Id ? d with { TextUnits = d.TextUnits.Select(u => u.Id == otherUnit.Id ? u with { Tokens = u.Tokens.Take(u.Tokens.Count - 1).Append(altered).ToArray() } : u).ToArray() } : d).ToArray() });
        Assert.Null(modified.Audio(other));
    }
}
