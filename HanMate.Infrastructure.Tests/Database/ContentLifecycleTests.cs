using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Contracts;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Pinyin;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class ContentLifecycleTests
{
    [Fact]
    public async Task TrashHidesSearchLearningAndFavoritesThenRestoresSameGraph()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var snapshot = await Personal(db); var id = snapshot.Document.Id;
        var favorites = new FavoriteStore(db); var folder = Assert.Single(await favorites.GetFoldersAsync());
        await favorites.SetSelectionAsync(id, [folder.Id], snapshot.MembershipRevision);
        var editing = await new EditorCommitStore(db).StartAsync(snapshot);
        var trash = new ContentTrashStore(db); var plan = await trash.PreviewAsync(id);
        Assert.Equal(1, plan.Dependencies.Favorites); Assert.Equal(1, plan.Dependencies.Drafts);
        await trash.MoveAsync(plan);
        Assert.Empty((await new LearningCatalogStore(db).QueryAsync(new(ContentKind.Word))).Items);
        Assert.Empty((await new OfflineSearchStore(db).SearchAsync("你好")).Items);
        Assert.Empty(await favorites.GetEntriesAsync(folder.Id)); Assert.Equal(0, Assert.Single(await favorites.GetFoldersAsync()).Count);
        Assert.Single(await new TextDraftStore(db).ListAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => new EditorCommitStore(db).CommitAsync(editing));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalAudioStore(db).GetTargetAsync(snapshot.Document.TextUnits[0].Id));
        var row = Assert.Single(await trash.ListAsync()); await trash.RestoreAsync(row);
        Assert.Empty(await trash.ListAsync()); Assert.Single((await new OfflineSearchStore(db).SearchAsync("你好")).Items);
        Assert.Single((await new LearningCatalogStore(db).QueryAsync(new(ContentKind.Word))).Items);
        Assert.Single(await favorites.GetEntriesAsync(folder.Id));
        var restored = (await new SqliteContentDocumentStore(db, new()).GetAsync(id))!;
        Assert.Equal(JsonSerializer.Serialize(snapshot.Document, ContentJson.Options), JsonSerializer.Serialize(restored.Document, ContentJson.Options));
        Assert.Equal(snapshot.RowRevision + 2, restored.RowRevision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => trash.RestoreAsync(row));
    }
    [Theory]
    [InlineData("favorite")][InlineData("draft")][InlineData("edit")]
    public async Task TrashPreviewMustStillMatchAtCommit(string change)
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var snapshot = await Personal(db);
        var trash = new ContentTrashStore(db); var plan = await trash.PreviewAsync(snapshot.Document.Id);
        if (change == "favorite") { var store = new FavoriteStore(db); var folder = Assert.Single(await store.GetFoldersAsync()); await store.SetSelectionAsync(snapshot.Document.Id, [folder.Id], 1); }
        else if (change == "draft") await new EditorCommitStore(db).StartAsync(snapshot);
        else await new SqliteContentDocumentStore(db, new()).SaveAsync(snapshot.Document with { Title = "changed" }, snapshot.RowRevision);
        await Assert.ThrowsAsync<RevisionConflictException>(() => trash.MoveAsync(plan)); Assert.Empty(await trash.ListAsync());
    }
    [Fact]
    public async Task FailedTrashCommitRollsBackMarkerAndRevision()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var snapshot = await Personal(db);
        var trash = new ContentTrashStore(db); var plan = await trash.PreviewAsync(snapshot.Document.Id);
        await Execute(db, "CREATE TRIGGER fail_epoch BEFORE UPDATE ON app_state BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => trash.MoveAsync(plan)); Assert.Empty(await trash.ListAsync());
        Assert.Equal(snapshot.RowRevision, (await new SqliteContentDocumentStore(db, new()).GetAsync(snapshot.Document.Id))!.RowRevision);
    }
    [Theory]
    [InlineData("favorite")][InlineData("audio")][InlineData("draft")][InlineData("token")][InlineData("modified")][InlineData("teaching")]
    public async Task RetainingUninstallPreservesDependenciesAndExactReinstallAdoptsIds(string dependency)
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var catalog = new BundledResourceCatalog(db, new(db)); await catalog.EnsureInstalledAsync();
        var management = new ResourceManagementStore(db); var resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        var entry = (await management.GetEntriesAsync(resource.State.ResourceId))[0]; var doc = JsonSerializer.Deserialize<ContentDocument>(entry.BodyJson, ContentJson.Options)!;
        var sql = dependency switch {
            "favorite" => "INSERT INTO favorite_folder(id,name,name_key) VALUES('f','f','f'); INSERT INTO favorite_item VALUES('f',$id,0,'2026-09-18T00:00:00Z');",
            "audio" => "INSERT INTO audio_asset VALUES('asset',printf('%064d',0),'user.wav',100,100,'wav','pcm',16000,1,'user'); INSERT INTO audio_binding VALUES('binding',$unit,'asset',printf('%064d',0),printf('%064d',0),'confirmed','user','');",
            "draft" => "INSERT INTO draft VALUES('d',$id,'content',1,'{}','2026-09-18T00:00:00Z');",
            "token" => "INSERT INTO draft VALUES('d',NULL,'audio',1,json_object('Token',$token),'2026-09-18T00:00:00Z');",
            "modified" => "UPDATE content SET row_revision=row_revision+1 WHERE id=$id;",
            _ => "INSERT INTO pinyin_item VALUES('p','p','p',0,json_object('unit',$unit));"
        };
        await Execute(db, sql, ("$id", doc.Id.ToString()), ("$unit", doc.TextUnits[0].Id.ToString()), ("$token", doc.TextUnits[0].Tokens[0].Id.ToString().ToUpperInvariant()));
        var plan = await management.PreviewRetainingUninstallAsync(resource); Assert.Equal(1, plan.RetainedCount); Assert.Equal(2, plan.DeletedCount);
        await management.UninstallRetainingAsync(plan);
        Assert.Empty(await management.GetEntriesAsync(resource.State.ResourceId)); Assert.Single(await management.GetRetainedAsync());
        var saved = (await new SqliteContentDocumentStore(db, new()).GetAsync(doc.Id))!.Document;
        Assert.Equal(ContentOrigin.Retained, saved.Origin); Assert.Equal(doc.Source, saved.Source);
        Assert.Equal(doc.TextUnits[0].Id, saved.TextUnits[0].Id);
        await catalog.RestoreAsync(resource.State.ResourceId);
        Assert.Empty(await management.GetRetainedAsync()); Assert.Equal(3, (await management.GetEntriesAsync(resource.State.ResourceId)).Count);
        saved = (await new SqliteContentDocumentStore(db, new()).GetAsync(doc.Id))!.Document;
        Assert.Equal(ContentOrigin.Resource, saved.Origin); Assert.Equal(doc.TextUnits[0].Id, saved.TextUnits[0].Id);
        if (dependency == "audio") Assert.Equal(1L, await Scalar(db, "SELECT count(*) FROM audio_binding WHERE id='binding'"));
        if (dependency is "draft" or "token") Assert.Equal(1L, await Scalar(db, "SELECT count(*) FROM draft WHERE id='d'"));
    }
    [Fact]
    public async Task RetainingUninstallRechecksReferencesAndReceiptFailureRollsBack()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var management = new ResourceManagementStore(db); var resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        var entry = (await management.GetEntriesAsync(resource.State.ResourceId))[0]; var plan = await management.PreviewRetainingUninstallAsync(resource);
        await Execute(db, "INSERT INTO draft VALUES('d',$id,'content',1,'{}','2026-09-18T00:00:00Z');", ("$id", entry.ContentId.ToString()));
        Assert.Equal("PLAN_STALE", (await Assert.ThrowsAsync<PackageException>(() => management.UninstallRetainingAsync(plan))).Code);
        plan = await management.PreviewRetainingUninstallAsync(resource);
        await Execute(db, "CREATE TRIGGER fail_receipt BEFORE INSERT ON resource_operation BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => management.UninstallRetainingAsync(plan));
        Assert.Empty(await management.GetRetainedAsync()); Assert.Equal(resource, Assert.Single(await management.GetAsync(ResourceKind.Dictionary)));
    }
    [Fact]
    public async Task ModifiedRetainedBodyCannotBeOverwrittenByReinstall()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); var catalog = new BundledResourceCatalog(db, new(db)); await catalog.EnsureInstalledAsync();
        var management = new ResourceManagementStore(db); var resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary)); var entry = (await management.GetEntriesAsync(resource.State.ResourceId))[0];
        await Execute(db, "UPDATE content SET row_revision=row_revision+1 WHERE id=$id", ("$id", entry.ContentId.ToString()));
        await management.UninstallRetainingAsync(await management.PreviewRetainingUninstallAsync(resource));
        var store = new SqliteContentDocumentStore(db, new()); var saved = (await store.GetAsync(entry.ContentId))!;
        await store.SaveAsync(saved.Document with { Title = "modified retained" }, saved.RowRevision);
        Assert.Equal("RESOURCE_CONTENT_CONFLICT", (await Assert.ThrowsAsync<PackageException>(() => catalog.RestoreAsync(resource.State.ResourceId))).Code);
        Assert.Equal("modified retained", (await store.GetAsync(entry.ContentId))!.Document.Title);
        Assert.False(Assert.Single(await management.GetAsync(ResourceKind.Dictionary)).State.IsPresent);
    }
    [Fact]
    public async Task EntryWithdrawalRejectsNonexistentEntriesAndRestoresSearch()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath); await new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
        var management = new ResourceManagementStore(db); var states = new ResourceStateStore(db); var resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        await Assert.ThrowsAsync<RevisionConflictException>(() => states.SetEntryRemovedAsync(resource.State.ResourceId, "missing", true, resource.State.RowRevision, DateTimeOffset.UtcNow,
            new(Guid.NewGuid(), resource.State.ResourceId, ResourceOperationType.Remove, resource.Epoch, DateTimeOffset.UtcNow, "{}")));
        Assert.Equal(resource, Assert.Single(await management.GetAsync(ResourceKind.Dictionary)));
        var entry = (await management.GetEntriesAsync(resource.State.ResourceId))[0];
        await states.SetEntryRemovedAsync(resource.State.ResourceId, entry.EntryId, true, resource.State.RowRevision, DateTimeOffset.UtcNow,
            new(Guid.NewGuid(), resource.State.ResourceId, ResourceOperationType.Remove, resource.Epoch, DateTimeOffset.UtcNow, "{}"));
        Assert.True((await management.GetEntriesAsync(resource.State.ResourceId))[0].Removed);
        Assert.DoesNotContain(entry.ContentId, await states.GetQueryableContentIdsAsync(ResourceKind.Dictionary));
        resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        await states.SetEntryRemovedAsync(resource.State.ResourceId, entry.EntryId, false, resource.State.RowRevision, DateTimeOffset.UtcNow,
            new(Guid.NewGuid(), resource.State.ResourceId, ResourceOperationType.RestoreEntry, resource.Epoch, DateTimeOffset.UtcNow, "{}"));
        Assert.Contains(entry.ContentId, await states.GetQueryableContentIdsAsync(ResourceKind.Dictionary));
    }
    [Fact]
    public async Task PendingAudioDraftPreventsGrammarUnitDeletionAtCommit()
    {
        using var dir = new TestDatabaseDirectory(); var db = new HanMateDatabase(dir.DatabasePath);
        var body = new TextDraft("grammar", "说明", ContentKind.Grammar, "textDraft.v2") { Pattern = "{S}是{N}", Example = "你好" };
        var doc = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document;
        var input = GrammarEditing.From(doc); input = input with { Units = [.. input.Units, new(Guid.NewGuid(), TextUnitRole.Example, "第二例句", new Dictionary<string,string>())] };
        doc = GrammarEditing.Apply(doc, input, BundledAnnotationLexicon.Default.Engine).Document;
        var drafts = new TextDraftStore(db); var editor = new EditorCommitStore(db);
        var saved = await editor.CommitAsync(await drafts.SaveAsync(Guid.NewGuid(), body with { Annotation = doc }, 0));
        var target = doc.TextUnits.Last().Id;
        await Execute(db, "INSERT INTO draft VALUES('recording',$id,'audio',1,json_object('Target',json_object('Id',$unit)),'2026-09-18T00:00:00Z')", ("$id", doc.Id.ToString()), ("$unit", target.ToString().ToUpperInvariant()));
        var draft = await editor.StartAsync(saved); input = GrammarEditing.From(doc); input = input with { Units = input.Units.Where(u => u.Id != target).ToArray() };
        doc = GrammarEditing.Apply(doc, input, BundledAnnotationLexicon.Default.Engine).Document;
        draft = await drafts.SaveAsync(draft.Id, draft.Body with { Annotation = doc }, draft.Revision);
        Assert.Equal("EDITOR_AUDIO_TARGET_PROTECTED", (await Assert.ThrowsAsync<InvalidOperationException>(() => editor.CommitAsync(draft))).Message);
        Assert.Equal(saved.RowRevision, (await new SqliteContentDocumentStore(db, new()).GetAsync(doc.Id))!.RowRevision);
        Assert.Single(await drafts.ListAsync());
    }
    private static async Task<ContentDocumentSnapshot> Personal(HanMateDatabase db)
    {
        var body = new TextDraft("Personal test", "你好", ContentKind.Word, "textDraft.v2");
        body = body with { Annotation = DraftAnnotation.Generate(body, BundledAnnotationLexicon.Default.Engine).Document };
        return await new EditorCommitStore(db).CommitAsync(await new TextDraftStore(db).SaveAsync(Guid.NewGuid(), body, 0));
    }
    private static async Task Execute(HanMateDatabase db, string sql, params (string, object)[] parameters)
    {
        using var c = await db.OpenConnectionAsync(); using var command = c.CreateCommand(); command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value); await command.ExecuteNonQueryAsync();
    }
    private static async Task<object?> Scalar(HanMateDatabase db, string sql)
    { using var c = await db.OpenConnectionAsync(); using var command = c.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
}
