using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class ReadingAudioTests
{
    [Fact]
    public async Task HeadwordRecordingReplacesOnlyMissingSpeechAndRetainsStaleReadingGuard()
    {
        using var area = new Area();
        var doc = await area.Seed(ContentKind.Word, "银行");
        var reading = new ReadingDocument(doc);
        var unit = doc.TextUnits.Single(u => u.Role == TextUnitRole.Headword);
        var asset = new HanMate.Core.Pinyin.PronunciationAsset("bank", "WordAudio/bank.mp3", new string('a', 64), new string('a', 64),
            1, "https://example.org/source", "https://example.org/audio", "Test fixture", "Test fixture", "https://example.org/license",
            "Test binding only", "None", "needsReview", unit.Text, string.Join(' ', unit.Tokens.Select(t => t.Pinyin!.Base + t.Pinyin.Tone)));
        var catalog = new WordRecordingCatalog(new("test", [asset]));
        var original = await area.Plans.PlanAsync(reading, new(unit.Id, null), allowSpeechFallback: true);
        var mapped = original.WithWordRecordings(reading, catalog);
        Assert.Empty(mapped.MissingTargets);
        var step = Assert.Single(mapped.Steps);
        Assert.Equal("recorded-word:bank:" + original.Steps[0].AssetKey, step.AssetKey);
        Assert.Equal("银行", await area.Plans.ReadSpeechAsync(original.Steps[0].AssetKey));

        await area.Record(unit.Id);
        var local = await area.Plans.PlanAsync(reading, new(unit.Id, null), allowSpeechFallback: true);
        Assert.StartsWith("local:checked:", Assert.Single(local.WithWordRecordings(reading, catalog).Steps).AssetKey);

        var editor = new EditorCommitStore(area.Db);
        var draft = await editor.StartAsync((await area.Documents.GetAsync(doc.Id))!);
        var changed = draft.Body with { Annotation = DraftAnnotation.Correct(draft.Body.Annotation!, unit.Tokens[^1].Id, "xing2") };
        await editor.CommitAsync(await new TextDraftStore(area.Db).SaveAsync(draft.Id, changed, draft.Revision));
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Plans.ReadSpeechAsync(original.Steps[0].AssetKey));
        var current = new ReadingDocument((await area.Documents.GetAsync(doc.Id))!.Document);
        var fallback = (await area.Plans.PlanAsync(current, new(unit.Id, null), allowSpeechFallback: true)).WithWordRecordings(current, catalog);
        Assert.StartsWith("speech:checked:", Assert.Single(fallback.Steps).AssetKey);
        Assert.Single(fallback.MissingTargets);
    }

    [Theory]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    [InlineData(ContentKind.Grammar)]
    public async Task HeadwordCatalogDoesNotReplacePassageOrGrammarPlayback(ContentKind kind)
    {
        using var area = new Area(); var doc = await area.Seed(kind, "银行"); var reading = new ReadingDocument(doc);
        var plan = await area.Plans.PlanAsync(reading, null, allowSpeechFallback: true);
        Assert.Same(plan, plan.WithWordRecordings(reading, new(new("empty", []))));
    }

    [Theory]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    public async Task BundledLessonRowsPlayExactlyTheirSavedSegmentsAndFullQueueKeepsOrder(ContentKind kind)
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        await new HanMate.Infrastructure.Catalog.BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var rows = await new LearningCatalogStore(db).QueryAsync(new(kind));
        Assert.NotEmpty(rows.Items);
        var audio = new ReadingAudioStore(db);
        foreach (var row in rows.Items)
        {
            var document = System.Text.Json.JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
            var reading = new ReadingDocument(document);
            var blocks = LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.English);
            if (document.Source.SourceId == "hanmate-common-lessons")
            {
                Assert.All(document.TextUnits.SelectMany(u => u.Tokens).Where(t => t.Kind == TokenKind.Hanzi), t => Assert.NotNull(t.Pinyin));
                Assert.All(reading.Targets, target =>
                {
                    var segment = reading.Segment(target)!;
                    Assert.False(string.IsNullOrWhiteSpace(segment.Translations["en"]));
                    Assert.False(string.IsNullOrWhiteSpace(segment.Translations["ja"]));
                });
                Assert.Equal(reading.Targets.Count, blocks.Count(b => b.Translation is not null));
                Assert.All(LessonPresentation.Create(reading, HanMate.Core.Localization.UiLanguage.ChineseSimplified), b => Assert.Null(b.Translation));
                if (kind == ContentKind.Poem)
                {
                    Assert.InRange(reading.Targets.Count, 3, 4);
                    Assert.Equal(reading.Targets.Count - 1, blocks.SelectMany(b => b.Atoms).Count(a => a.HardBreak));
                }
            }
            Assert.Equal(reading.Targets, blocks.Where(b => b.Target is not null).Select(b => b.Target).Distinct());
            foreach (var target in reading.Targets)
            {
                var single = Assert.Single((await audio.PlanAsync(reading, target, allowSpeechFallback: true)).Steps);
                Assert.Equal(target.SegmentId, single.TargetId);
                Assert.Equal(reading.Segment(target)!.Text, await audio.ReadSpeechAsync(single.AssetKey));
            }
            var all = await audio.PlanAsync(reading, null, allowSpeechFallback: true);
            Assert.Equal(reading.Targets.Select(t => t.SegmentId), all.Steps.Select(s => (Guid?)s.TargetId));
            Assert.Equal(document.TextUnits.SelectMany(u => u.Segments).Where(s => s.Kind == SegmentKind.Speech).Select(s => s.Text), all.Steps.Select(s => s.Text));
        }
        var added = rows.Items.Select(row => System.Text.Json.JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!)
            .Where(d => d.Source.SourceId == "hanmate-common-lessons").Select(d => d.Title).Order().ToArray();
        Assert.Equal((kind == ContentKind.Poem
            ? new[] { "静夜思", "春晓", "咏鹅", "悯农（其二）", "登鹳雀楼", "江雪", "离骚（节选）", "鱼和熊掌不可兼得（《鱼我所欲也》节选）", "相思", "鹿柴" }
            : new[] { "自我介绍", "我的家", "一天的生活", "在学校", "去买东西", "问路", "买菜对话", "点餐对话", "借书对话" }).Order(), added);
    }

    [Fact]
    public async Task BundledGrammarDetailsPreserveOrderedUnitsAndPlayOnlyTheSelectedWholeExample()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        await new HanMate.Infrastructure.Catalog.BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var rows = await new LearningCatalogStore(db).QueryAsync(new(ContentKind.Grammar));
        Assert.NotEmpty(rows.Items);
        var added = rows.Items.Select(row => System.Text.Json.JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!)
            .Where(d => d.Source.SourceId == "hanmate-common-grammar").Select(d => d.Title).Order().ToArray();
        Assert.Equal(new[] { "“有”表示拥有", "“会”表示学会的能力", "“想”表达愿望", "因为……所以……", "一边……一边……" }.Order(), added);
        var audio = new ReadingAudioStore(db);
        foreach (var row in rows.Items)
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
            var grammar = doc.Grammar!;
            // Source-array ordering is not the presentation contract; the stable ID lists are.
            var reading = new ReadingDocument(doc with { TextUnits = doc.TextUnits.Reverse().ToArray() });
            var ordered = grammar.ExplanationUnitIds.Concat(grammar.ExampleUnitIds).Concat(grammar.NoteUnitIds).ToArray();
            Assert.Equal(ordered, reading.Pages.SelectMany(p => p.Parts).Where(p => p.StartsUnit).Select(p => p.Unit.Id));
            Assert.Equal(ordered, reading.Targets.Select(t => t.UnitId));
            Assert.All(reading.Targets, target => Assert.Null(target.SegmentId));
            // Playback must use the exact saved snapshot, not the reordered projection fixture.
            var savedReading = new ReadingDocument(doc);
            foreach (var id in grammar.ExampleUnitIds)
            {
                var example = doc.TextUnits.Single(u => u.Id == id);
                var plan = await audio.PlanAsync(savedReading, new(id, null), allowSpeechFallback: true);
                var step = Assert.Single(plan.Steps);
                Assert.Equal(id, step.TargetId);
                Assert.Equal(example.Text, await audio.ReadSpeechAsync(step.AssetKey));
                foreach (var language in new[] { HanMate.Core.Localization.UiLanguage.English, HanMate.Core.Localization.UiLanguage.Japanese })
                {
                    Assert.NotNull(HanMate.Core.Localization.UiLanguagePolicy.SelectAuxiliaryTranslation(grammar.PatternTranslations, language));
                    Assert.NotNull(HanMate.Core.Localization.UiLanguagePolicy.SelectAuxiliaryTranslation(example.Translations, language));
                }
                Assert.Null(HanMate.Core.Localization.UiLanguagePolicy.SelectAuxiliaryTranslation(example.Translations, HanMate.Core.Localization.UiLanguage.ChineseSimplified));
            }
        }
    }

    [Fact]
    public async Task BundledLearningListCanPlayEachHeadwordWithoutReadingItsDefinitionOrExamples()
    {
        using var directory = new TestDatabaseDirectory(); var db = new HanMateDatabase(directory.DatabasePath);
        await new HanMate.Infrastructure.Catalog.BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var catalog = new LearningCatalogStore(db); var audio = new ReadingAudioStore(db);
        var first = await catalog.QueryAsync(new(ContentKind.Word));
        var rows = first.Items.ToList();
        for (var offset = 50; offset < first.Total; offset += 50)
            rows.AddRange((await catalog.QueryAsync(new(ContentKind.Word), offset)).Items);
        Assert.Equal(first.Total, rows.Count);
        foreach (var row in rows)
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
            var head = doc.TextUnits.Single(u => u.Role == TextUnitRole.Headword);
            Assert.NotNull(HanMate.Core.Localization.UiLanguagePolicy.SelectAuxiliaryTranslation(head.Translations, HanMate.Core.Localization.UiLanguage.English));
            Assert.NotNull(HanMate.Core.Localization.UiLanguagePolicy.SelectAuxiliaryTranslation(head.Translations, HanMate.Core.Localization.UiLanguage.Japanese));
            Assert.Null(HanMate.Core.Localization.UiLanguagePolicy.SelectAuxiliaryTranslation(head.Translations, HanMate.Core.Localization.UiLanguage.ChineseSimplified));
            Assert.Contains(doc.TextUnits, u => u.Role == TextUnitRole.Definition);
            var plan = await audio.PlanAsync(new(doc), new(head.Id, null), allowSpeechFallback: true);
            var step = Assert.Single(plan.Steps);
            Assert.Equal(head.Id, step.TargetId); Assert.Equal(head.Text, step.Text);
            Assert.Equal(head.Text, await audio.ReadSpeechAsync(step.AssetKey));
            if (head.Tokens.Count is > 0 and <= 32 && head.Tokens.All(t => t.Kind == TokenKind.Hanzi && t.Pinyin is { Erhua: false }))
                Assert.NotEmpty(PinyinVoiceInput.Example(head));
        }
    }

    [Fact]
    public async Task ExplicitSpeechFallbackPreservesMixedOrderAndValidatesAgainBeforeSpeaking()
    {
        using var area = new Area(); var doc = await area.Seed(ContentKind.Text, "你好。世界。再见。"); var reading = new ReadingDocument(doc);
        await area.Record(reading.Targets[1].SegmentId!.Value);
        var plan = await area.Plans.PlanAsync(reading, null, allowSpeechFallback: true);
        Assert.Equal(3, plan.Steps.Count); Assert.Equal(2, plan.MissingTargets.Count);
        Assert.StartsWith("speech:checked:", plan.Steps[0].AssetKey); Assert.StartsWith("local:checked:", plan.Steps[1].AssetKey);
        Assert.Equal(reading.Targets.Select(t => t.SegmentId!.Value), plan.Steps.Select(s => s.TargetId));
        Assert.Equal(plan.Steps[0].Text, await area.Plans.ReadSpeechAsync(plan.Steps[0].AssetKey));
        await area.Execute("UPDATE playback_target SET pronunciation_hash='0000000000000000000000000000000000000000000000000000000000000000'");
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Plans.ReadSpeechAsync(plan.Steps[0].AssetKey));
    }
    [Theory][InlineData(ContentKind.Word)][InlineData(ContentKind.Grammar)]
    public async Task SpeechNeverReadsGrammarSlotsAndTrashInvalidatesPreparedSpeech(ContentKind kind)
    {
        using var area = new Area(); var doc = await area.Seed(kind, "你好，世界。"); var reading = new ReadingDocument(doc);
        Assert.Empty((await area.Plans.PlanAsync(reading, null)).Steps);
        var plan = await area.Plans.PlanAsync(reading, null, allowSpeechFallback: true);
        foreach (var step in plan.Steps) { Assert.DoesNotContain("主语", step.Text); Assert.Equal(step.Text, await area.Plans.ReadSpeechAsync(step.AssetKey)); }
        var trash = new ContentTrashStore(area.Db); await trash.MoveAsync(await trash.PreviewAsync(doc.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Plans.ReadSpeechAsync(plan.Steps[0].AssetKey));
    }
    [Theory]
    [InlineData(ContentKind.Word)]
    [InlineData(ContentKind.Grammar)]
    public async Task WordAndGrammarPlayWholeUnitIncludingCommaAndNeverPatternSlots(ContentKind kind)
    {
        using var area = new Area(); var doc = await area.Seed(kind, "你好，世界。"); var reading = new ReadingDocument(doc);
        foreach (var unit in doc.TextUnits) await area.Record(unit.Id);
        var plan = await area.Plans.PlanAsync(reading, null); Assert.Empty(plan.MissingTargets);
        Assert.Equal(reading.Targets.Select(t => t.UnitId), plan.Steps.Select(s => s.TargetId));
        Assert.All(plan.Steps, step => Assert.Equal(step.UnitId, step.TargetId));
        var example = kind == ContentKind.Grammar ? reading.Targets.Last() : reading.Targets.First();
        var one = Assert.Single((await area.Plans.PlanAsync(reading, example)).Steps);
        Assert.Equal("你好，世界。", one.Text); Assert.DoesNotContain("主语", one.Text);
        await area.Read(plan.Steps[0]);
    }

    [Theory]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    public async Task WholeUnitRecordingDoesNotPretendToBeSegmentAudio(ContentKind kind)
    {
        using var area = new Area(); var doc = await area.Seed(kind, "你好，\r\n\r\n世界。"); var reading = new ReadingDocument(doc);
        await area.Record(doc.TextUnits[0].Id);
        var all = await area.Plans.PlanAsync(reading, null); var full = Assert.Single(all.Steps);
        Assert.Empty(all.MissingTargets); Assert.Equal(doc.TextUnits[0].Text, full.Text); Assert.Equal(full.UnitId, full.TargetId);
        var part = await area.Plans.PlanAsync(reading, reading.Targets[0]);
        Assert.Empty(part.Steps); Assert.Equal(reading.Targets[0].SegmentId, Assert.Single(part.MissingTargets));
    }

    [Theory]
    [InlineData(ContentKind.Text)]
    [InlineData(ContentKind.Poem)]
    public async Task SegmentQueuePreservesOrderAndSkipsOnlyLayout(ContentKind kind)
    {
        using var area = new Area(); var doc = await area.Seed(kind, "你好，\r\n\r\n世界。"); var reading = new ReadingDocument(doc);
        foreach (var target in reading.Targets) await area.Record(target.SegmentId!.Value);
        var plan = await area.Plans.PlanAsync(reading, null); Assert.Empty(plan.MissingTargets);
        Assert.Equal(reading.Targets.Select(t => t.SegmentId!.Value), plan.Steps.Select(s => s.TargetId));
        Assert.All(plan.Steps, s => Assert.NotEqual(s.UnitId, s.TargetId));
        var only = await area.Plans.PlanAsync(reading, reading.Targets.Last()); Assert.Single(only.Steps);
        Assert.Equal(reading.Segment(reading.Targets.Last())!.Text, only.Steps[0].Text);
        Assert.Equal(doc.TextUnits[0].Text, string.Concat(reading.Pages.SelectMany(p => p.Parts).SelectMany(p => p.Atoms).Select(a => a.Text)));
    }

    [Fact]
    public async Task MissingAudioIsReportedBeforeQueueCanStartAndPendingDraftIsNotAudio()
    {
        using var area = new Area(); var doc = await area.Seed(ContentKind.Text, "你好。世界。再见。"); var reading = new ReadingDocument(doc);
        await area.Record(reading.Targets[0].SegmentId!.Value);
        await area.Audio.BeginAsync(await area.Audio.GetTargetAsync(reading.Targets[1].SegmentId!.Value), "user");
        var plan = await area.Plans.PlanAsync(reading, null); Assert.Single(plan.Steps);
        Assert.Equal(reading.Targets.Skip(1).Select(t => t.SegmentId!.Value), plan.MissingTargets);
    }

    [Fact]
    public async Task StaleReaderCannotPlayAfterContentEditAndOldPlanCannotPlayChangedPronunciation()
    {
        using var area = new Area(); var doc = await area.Seed(ContentKind.Word, "你好"); var reading = new ReadingDocument(doc);
        await area.Record(doc.TextUnits[0].Id); var plan = await area.Plans.PlanAsync(reading, null);
        var snapshot = (await area.Documents.GetAsync(doc.Id))!;
        var editor = new EditorCommitStore(area.Db); var draft = await editor.StartAsync(snapshot);
        var changed = draft.Body with { Annotation = DraftAnnotation.Correct(draft.Body.Annotation!, doc.TextUnits[0].Tokens[0].Id, "ni4") };
        await editor.CommitAsync(await new TextDraftStore(area.Db).SaveAsync(draft.Id, changed, draft.Revision));
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Plans.PlanAsync(reading, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Read(plan.Steps[0]));
        var current = new ReadingDocument((await area.Documents.GetAsync(doc.Id))!.Document);
        Assert.Single((await area.Plans.PlanAsync(current, null)).MissingTargets);
    }

    [Fact]
    public async Task RemovedBindingOrTrashAfterPreviewCannotPlayAndLeaseIsReleased()
    {
        using var area = new Area(); var doc = await area.Seed(ContentKind.Word, "你好"); var reading = new ReadingDocument(doc);
        var target = await area.Audio.GetTargetAsync(doc.TextUnits[0].Id); var binding = await area.Record(target.Id);
        var plan = await area.Plans.PlanAsync(reading, null);
        await area.Audio.UpdateTrackAsync(target, binding, "remove");
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Read(plan.Steps[0]));
        await area.Record(target.Id); var trash = new ContentTrashStore(area.Db); await trash.MoveAsync(await trash.PreviewAsync(doc.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Plans.PlanAsync(reading, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => area.Read(plan.Steps[0]));
        Assert.Equal(0L, await area.Scalar("SELECT count(*) FROM file_lease"));
    }

    [Fact]
    public async Task ResourcePracticeRequiresExplicitDefaultAndNeedsReviewNeverAutoPlays()
    {
        using var area = new Area(); var doc = await area.Seed(ContentKind.Word, "你好"); var reading = new ReadingDocument(doc);
        var binding = await area.Record(doc.TextUnits[0].Id, false);
        // Exercise automatic source policy independently of the resource installer.
        await area.Execute("UPDATE content SET origin='resource'");
        Assert.Single((await area.Plans.PlanAsync(reading, null)).MissingTargets);
        await area.Audio.UpdateTrackAsync(await area.Audio.GetTargetAsync(doc.TextUnits[0].Id), binding, "default");
        Assert.Single((await area.Plans.PlanAsync(reading, null)).Steps);
        await area.Execute("UPDATE audio_binding SET review_state='needsReview'");
        Assert.Single((await area.Plans.PlanAsync(reading, null)).MissingTargets);
    }

    [Fact]
    public async Task UnknownTargetAndCancelledPlanAreRejected()
    {
        using var area = new Area(); var doc = await area.Seed(ContentKind.Text, "你好。世界。"); var reading = new ReadingDocument(doc);
        await Assert.ThrowsAsync<ArgumentException>(() => area.Plans.PlanAsync(reading, new(Guid.NewGuid(), null)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => area.Plans.PlanAsync(reading, null, cancellation.Token));
    }

    private sealed class Area : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "HanMateReadingAudioTests", Guid.NewGuid().ToString("N"));
        public HanMateDatabase Db { get; }
        public LocalAudioStore Audio { get; }
        public ReadingAudioStore Plans { get; }
        public SqliteContentDocumentStore Documents { get; }
        public Area() { Directory.CreateDirectory(_path); Db = new(Path.Combine(_path, "test.db")); Audio = new(Db); Plans = new(Db); Documents = new(Db, new()); }
        public async Task<ContentDocument> Seed(ContentKind kind, string text)
        {
            var draft = new TextDraft("Reading audio test", text, kind, "textDraft.v2") { Pattern = "{主语}+你好", Example = kind == ContentKind.Grammar ? text : "" };
            draft = draft with { Annotation = DraftAnnotation.Generate(draft, BundledAnnotationLexicon.Default.Engine).Document };
            return (await new EditorCommitStore(Db).CommitAsync(await new TextDraftStore(Db).SaveAsync(Guid.NewGuid(), draft, 0))).Document;
        }
        public async Task<Guid> Record(Guid targetId, bool preferred = true)
        {
            using var wave = new MemoryStream(); PcmWave.WriteHeader(wave, 8000, 1, 16000); wave.Write(new byte[16000]); wave.Position = 0;
            return await Audio.SaveAsync(await Audio.ImportAsync(await Audio.GetTargetAsync(targetId), wave), "Engineering audio", preferred);
        }
        public async Task Read(ReadingAudioStep step)
        {
            var p = step.AssetKey.Split(':');
            await using var lease = await Audio.OpenExpectedPlaybackAsync(Guid.Parse(p[2]), Guid.Parse(p[3]), p[4], p[5]);
            Assert.Equal(1000, PcmWave.Inspect(lease.Stream).DurationMs);
        }
        public async Task Execute(string sql) { await using var c = await Db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
        public async Task<long> Scalar(string sql) { await using var c = await Db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return (long)(await cmd.ExecuteScalarAsync())!; }
        public void Dispose() => Directory.Delete(_path, true);
    }
}
