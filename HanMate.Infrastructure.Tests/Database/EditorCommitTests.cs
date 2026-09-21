using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Infrastructure.Pinyin;
using HanMate.Infrastructure.Database;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class EditorCommitTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void EmbeddedLexiconHasAuditableCoverageAndContextCandidates()
    {
        var lexicon = BundledAnnotationLexicon.Default;
        output.WriteLine($"Accepted: {lexicon.AcceptedReadings}; characters: {lexicon.AcceptedCharacters}; unsupported: {lexicon.UnsupportedReadings.Count}");
        output.WriteLine(string.Join("\n", lexicon.UnsupportedReadings));
        Assert.Equal(55328, lexicon.AcceptedReadings + lexicon.UnsupportedReadings.Count);
        Assert.True(lexicon.AcceptedReadings > 55000);
        var phrase = Assert.Single(lexicon.Engine.Annotate("银行"));
        Assert.Equal(new[] { "yín", "háng" }, phrase.Selected!.Syllables.Select(p => p.Display));
        Assert.Equal(AnnotationReviewState.NeedsReview, phrase.ReviewState);
        var ambiguous = Assert.Single(lexicon.Engine.Annotate("行"));
        Assert.Null(ambiguous.Selected); Assert.True(ambiguous.Candidates.Count > 1);
        Assert.Equal(AnnotationReviewState.Unknown, Assert.Single(lexicon.Engine.Annotate("㕶")).ReviewState);
        Assert.Equal("huī", Assert.Single(lexicon.Engine.Annotate("灰")).Selected!.Syllables[0].Display);
    }

    [Fact]
    public async Task CommitAtomicallyCreatesContentTargetsSearchAndCompletesDraft()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var draft = await Create(db, "你好", ContentKind.Word); var doc = draft.Body.Annotation!;
        foreach (var t in doc.TextUnits[0].Tokens) doc = DraftAnnotation.Correct(doc, t.Id, t.Pinyin!.Base + t.Pinyin.Tone);
        draft = await new TextDraftStore(db).SaveAsync(draft.Id, draft.Body with { Annotation = doc }, draft.Revision);
        var result = await new EditorCommitStore(db).CommitAsync(draft);
        Assert.Equal(1, result.RowRevision); Assert.Empty(await new TextDraftStore(db).ListAsync());
        Assert.Equal(1, await Count(db, "content")); Assert.Equal(2, await Count(db, "playback_target")); Assert.Equal(1, await Count(db, "search_index"));
        Assert.Equal("nihao", await Scalar(db, "SELECT pinyin_joined FROM search_index"));
        await Assert.ThrowsAsync<RevisionConflictException>(() => new EditorCommitStore(db).CommitAsync(draft));
        Assert.Equal(1, await Count(db, "content"));
    }

    [Fact]
    public async Task StaleDraftOrContentNeverPartiallyCommits()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var store = new TextDraftStore(db); var editor = new EditorCommitStore(db);
        var draft = await Create(db, "你好"); await store.SaveAsync(draft.Id, draft.Body, draft.Revision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => editor.CommitAsync(draft)); Assert.Equal(0, await Count(db, "content"));
        draft = Assert.Single(await store.ListAsync()); var committed = await editor.CommitAsync(draft);
        var editing = await editor.StartAsync(committed); var documents = new SqliteContentDocumentStore(db, new());
        await documents.SaveAsync(committed.Document with { Title = "changed elsewhere" }, committed.RowRevision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => editor.CommitAsync(editing));
        Assert.Single(await store.ListAsync()); Assert.Equal("changed elsewhere", (await documents.GetAsync(committed.Document.Id))!.Document.Title);
    }

    [Fact]
    public async Task AudioBindingIsRetainedAndOnlyChangedPronunciationBecomesNeedsReview()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var editor = new EditorCommitStore(db);
        var committed = await editor.CommitAsync(await Create(db, "你好。世界。")); var unit = committed.Document.TextUnits[0];
        await AddAudio(db, unit.Segments[0].Id); await AddAudio(db, unit.Segments[1].Id);
        var draft = await editor.StartAsync(committed);
        var changed = DraftAnnotation.Correct(draft.Body.Annotation!, unit.Tokens[0].Id, "ni4");
        draft = await new TextDraftStore(db).SaveAsync(draft.Id, draft.Body with { Annotation = changed }, draft.Revision);
        await editor.CommitAsync(draft);
        Assert.Equal(2, await Count(db, "audio_binding")); Assert.Equal(2, await Count(db, "audio_asset"));
        Assert.Equal("needsReview", await Scalar(db, $"SELECT review_state FROM audio_binding WHERE target_id='{unit.Segments[0].Id}'"));
        Assert.Equal("confirmed", await Scalar(db, $"SELECT review_state FROM audio_binding WHERE target_id='{unit.Segments[1].Id}'"));
    }

    [Fact]
    public async Task RemovingBoundTargetRollsBackContentIndexEpochAndDraft()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var editor = new EditorCommitStore(db);
        var committed = await editor.CommitAsync(await Create(db, "你好。世界。")); await AddAudio(db, committed.Document.TextUnits[0].Segments[0].Id);
        var draft = await editor.StartAsync(committed); var body = draft.Body with { Text = "再见。世界。" };
        body = body with { Annotation = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document };
        draft = await new TextDraftStore(db).SaveAsync(draft.Id, body, draft.Revision);
        var epoch = await Scalar(db, "SELECT data_epoch FROM app_state");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => editor.CommitAsync(draft)); Assert.Equal("EDITOR_AUDIO_TARGET_PROTECTED", ex.Message);
        Assert.Equal("你好。世界。", (await new SqliteContentDocumentStore(db, new()).GetAsync(committed.Document.Id))!.Document.TextUnits[0].Text);
        Assert.Equal(epoch, await Scalar(db, "SELECT data_epoch FROM app_state")); Assert.Single(await new TextDraftStore(db).ListAsync()); Assert.Equal(1, await Count(db, "audio_binding"));
    }

    [Fact]
    public async Task UniqueShiftKeepsBoundSegmentAndTitleChangeDoesNotInvalidateAudio()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var editor = new EditorCommitStore(db);
        var committed = await editor.CommitAsync(await Create(db, "你好。世界。")); var segment = committed.Document.TextUnits[0].Segments[1]; await AddAudio(db, segment.Id);
        var draft = await editor.StartAsync(committed); var body = draft.Body with { Title = "New title", Text = "前面。你好。世界。" };
        body = body with { Annotation = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document };
        await editor.CommitAsync(await new TextDraftStore(db).SaveAsync(draft.Id, body, draft.Revision));
        Assert.Equal("confirmed", await Scalar(db, $"SELECT review_state FROM audio_binding WHERE target_id='{segment.Id}'"));
    }

    [Fact]
    public async Task ResourceCopyRemapsEntireGrammarGraphAndPreservesSource()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var editor = new EditorCommitStore(db);
        var body = new TextDraft("是", "中文说明。", ContentKind.Grammar) { Pattern = "{主语} 是 {名词}", Example = "我是学生。" };
        var resource = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document with { Origin = ContentOrigin.Resource };
        var original = await new SqliteContentDocumentStore(db, new()).SaveAsync(resource, 0);
        var draft = await editor.StartAsync(original); Assert.Equal(0, draft.Body.ExpectedContentRevision);
        var result = await editor.CommitAsync(draft); Assert.NotEqual(resource.Id, result.Document.Id); Assert.Equal(ContentOrigin.Personal, result.Document.Origin);
        Assert.Empty(resource.TextUnits.SelectMany(u => u.Tokens.Select(t => t.Id).Append(u.Id).Concat(u.Segments.Select(s => s.Id)))
            .Intersect(result.Document.TextUnits.SelectMany(u => u.Tokens.Select(t => t.Id).Append(u.Id).Concat(u.Segments.Select(s => s.Id)))));
        Assert.Equal(resource.Source, result.Document.Source); Assert.True(new ContentDocumentValidator().Validate(result.Document).IsValid);
        Assert.Equal(4, await Count(db, "playback_target")); Assert.Equal(2, await Count(db, "content"));
    }

    [Fact]
    public async Task OldDraftsRemainReadableAndCancelledCommitLeavesDraft()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var store = new TextDraftStore(db);
        var old = await store.SaveAsync(Guid.NewGuid(), new("old", "raw"), 0); Assert.Equal("textDraft.v1", Assert.Single(await store.ListAsync()).Body.Format);
        await store.DeleteAsync(old); var draft = await Create(db, "你好"); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EditorCommitStore(db).CommitAsync(draft, cts.Token));
        Assert.Single(await store.ListAsync()); Assert.Equal(0, await Count(db, "content"));
    }

    [Fact]
    public async Task ChangedRawTextCannotCommitOldAnnotationAndForgedBodyCannotReplaceSavedDraft()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var store = new TextDraftStore(db); var editor = new EditorCommitStore(db);
        var draft = await Create(db, "你好");
        var stale = await store.SaveAsync(draft.Id, draft.Body with { Text = "世界" }, draft.Revision);
        await Assert.ThrowsAsync<InvalidDataException>(() => editor.CommitAsync(stale));
        await Assert.ThrowsAsync<RevisionConflictException>(() => editor.CommitAsync(stale with { Body = draft.Body }));
        Assert.Equal(0, await Count(db, "content")); Assert.Single(await store.ListAsync());
    }

    private static async Task<SavedTextDraft> Create(HanMateDatabase db, string text, ContentKind kind = ContentKind.Text)
    {
        var body = new TextDraft("Editor test", text, kind, "textDraft.v2");
        body = body with { Annotation = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document, EngineVersion = BundledAnnotationLexicon.Version };
        return await new TextDraftStore(db).SaveAsync(Guid.NewGuid(), body, 0);
    }
    private static async Task<long> Count(HanMateDatabase db, string table) => Convert.ToInt64(await Scalar(db, "SELECT COUNT(*) FROM " + table));
    private static async Task<object?> Scalar(HanMateDatabase db, string sql)
    { using var connection = await db.OpenConnectionAsync(); using var cmd = connection.CreateCommand(); cmd.CommandText = sql; return await cmd.ExecuteScalarAsync(); }
    private static async Task AddAudio(HanMateDatabase db, Guid target)
    {
        using var connection = await db.OpenConnectionAsync(); using var cmd = connection.CreateCommand(); var asset = Guid.NewGuid();
        cmd.CommandText = """
            INSERT INTO audio_asset(id,sha256,relative_path,byte_length,duration_ms,container,codec,sample_rate,channels,origin)
            VALUES($asset,$hash,'fixture.wav',100,100,'wav','pcm',16000,1,'user');
            INSERT INTO audio_binding(id,target_id,asset_id,bound_text_hash,bound_pronunciation_hash,review_state,source_role)
            SELECT $binding,id,$asset,text_hash,pronunciation_hash,'confirmed','user' FROM playback_target WHERE id=$target;
            """;
        cmd.Parameters.AddWithValue("$asset", asset.ToString()); cmd.Parameters.AddWithValue("$binding", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$hash", new string('a', 64)); cmd.Parameters.AddWithValue("$target", target.ToString()); await cmd.ExecuteNonQueryAsync();
    }
}
