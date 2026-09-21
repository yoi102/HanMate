using HanMate.App.Audio;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;

namespace HanMate.Core.Tests.Audio;

public sealed class PinyinSpeechRoutingTests
{
    private static PinyinCourse Course() => PinyinCourse.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/pinyin-course.json")));
    private sealed class Backend : IAudioPlaybackBackend
    {
        public List<string> Played { get; } = [];
        public Task PlayAsync(string key, CancellationToken token) { token.ThrowIfCancellationRequested(); Played.Add(key); return Task.CompletedTask; }
    }
    private sealed class Harness
    {
        public AiVoiceService Ai { get; } = new();
        public Backend Backend { get; } = new();
        public WordSpeechService Words { get; }
        public PinyinSpeechService Pinyin { get; }
        public Harness(SpeechEngine engine)
        {
            SpeechPreferences.Engine = engine;
            Preferences.Default.Set("pinyin.prefer-ai.v2", "true"); // Legacy preference must have no effect.
            NativeSpeech.Spoken.Clear(); BundledPronunciationService.Played.Clear();
            var text = new TextSpeechService(Ai);
            Words = new(text);
            Pinyin = new(Words, text, new(Backend), Backend);
        }
    }

    [Theory]
    [InlineData("local:test:recording")]
    [InlineData("existing-course-word")]
    public async Task ImportedAndCourseRecordingsRemainFirstEvenWithOldAiPreference(string key)
    {
        var h = new Harness(SpeechEngine.Kokoro);
        var unit = Course().Data.Contents.First().TextUnits[0];
        Assert.Equal(PlaybackOutcome.Completed, await h.Pinyin.PlayExampleAsync(Guid.NewGuid(), unit, key));
        Assert.Equal(key, Assert.Single(h.Backend.Played));
        Assert.Empty(h.Ai.Calls); Assert.Empty(NativeSpeech.Spoken); Assert.Empty(BundledPronunciationService.Played);
    }

    [Theory]
    [InlineData(SpeechEngine.Melo)]
    [InlineData(SpeechEngine.Kokoro)]
    [InlineData(SpeechEngine.System)]
    public async Task PreviouslyMissingCourseWordsUseSharedAudioCmnBeforeAnyVoice(SpeechEngine engine)
    {
        var h = new Harness(engine); h.Ai.Available = false;
        var course = Course(); var catalog = await h.Words.GetCatalogAsync();
        var additional = course.Data.Items.SelectMany(i => i.Examples).DistinctBy(e => e.UnitId)
            .Where(e => course.PlaybackKey(e) is null && catalog.Find(course.Unit(e)) is not null).ToArray();
        Assert.NotEmpty(additional);
        foreach (var example in additional)
            Assert.Equal(PlaybackOutcome.Completed, await h.Pinyin.PlayExampleAsync(Guid.NewGuid(), course.Unit(example), null));
        Assert.Equal(additional.Length, BundledPronunciationService.Played.Count);
        Assert.Empty(h.Ai.Calls); Assert.Empty(NativeSpeech.Spoken);
    }

    [Theory]
    [InlineData(SpeechEngine.Melo)]
    [InlineData(SpeechEngine.Kokoro)]
    [InlineData(SpeechEngine.System)]
    public async Task MissingRecordingsUseTheSelectedProvider(SpeechEngine engine)
    {
        var h = new Harness(engine); var course = Course(); var catalog = await h.Words.GetCatalogAsync();
        var example = course.Data.Items.SelectMany(i => i.Examples)
            .First(e => course.PlaybackKey(e) is null && catalog.Find(course.Unit(e)) is null);
        var unit = course.Unit(example);
        Assert.Equal(PlaybackOutcome.Completed, await h.Pinyin.PlayExampleAsync(Guid.NewGuid(), unit, null));
        Assert.Empty(BundledPronunciationService.Played);
        if (engine == SpeechEngine.System)
        { Assert.Equal(unit.Text, Assert.Single(NativeSpeech.Spoken)); Assert.Empty(h.Ai.Calls); }
        else
        {
            var call = Assert.Single(h.Ai.Calls); Assert.Equal(engine, call.Engine);
            Assert.Equal(engine == SpeechEngine.Melo ? PinyinVoiceInput.Example(unit) : unit.Text, call.Input);
            Assert.Empty(NativeSpeech.Spoken);
        }
    }

    [Theory]
    [InlineData(SpeechEngine.Kokoro)]
    [InlineData(SpeechEngine.System)]
    public async Task UnsupportedIsolatedFinalDoesNotSwitchBackToMeloOrReadLatinLetters(SpeechEngine engine)
    {
        var h = new Harness(engine);
        Assert.Null(await h.Pinyin.PlayDemoAsync(Guid.NewGuid(), "ong", () => PinyinVoiceInput.Syllable("ong", 1), null));
        Assert.Empty(h.Ai.Calls); Assert.Empty(NativeSpeech.Spoken);
    }

    [Fact]
    public async Task MeloStillSupportsIsolatedFinalWhenExplicitlySelected()
    {
        var h = new Harness(SpeechEngine.Melo);
        Assert.Equal(PlaybackOutcome.Completed, await h.Pinyin.PlayDemoAsync(Guid.NewGuid(), "ong", () => PinyinVoiceInput.Syllable("ong", 1), null));
        Assert.Equal((SpeechEngine.Melo, "^ ong1 #0"), Assert.Single(h.Ai.Calls));
    }

    [Fact]
    public async Task UnavailableSelectedPackDoesNotFallBackToADifferentPack()
    {
        var h = new Harness(SpeechEngine.Kokoro); h.Ai.Available = false;
        var course = Course(); var catalog = await h.Words.GetCatalogAsync();
        var example = course.Data.Items.SelectMany(i => i.Examples)
            .First(e => course.PlaybackKey(e) is null && catalog.Find(course.Unit(e)) is null);
        Assert.Null(await h.Pinyin.PlayExampleAsync(Guid.NewGuid(), course.Unit(example), null));
        Assert.Empty(h.Ai.Calls); Assert.Empty(NativeSpeech.Spoken); Assert.Empty(BundledPronunciationService.Played);
    }
}
