using System.Text.Json;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class ResourceManagementTests
{
    [Fact]
    public async Task PriorityIsPersistedChangesSearchOrderAndRejectsStaleActions()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath);
        await Seed(db); var management = new ResourceManagementStore(db); var search = new OfflineSearchStore(db);
        var dictionary = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        Assert.Equal(3, dictionary.Count); Assert.True(dictionary.TextBytes > 0);
        Assert.Contains("bundled", dictionary.Name("en"));
        Assert.Equal(BundledResourceCatalog.LearningId.ToString(), (await search.SearchAsync("学习")).Items[0].SourceId);
        await management.SetPriorityAsync(dictionary, 0);
        Assert.Equal(BundledResourceCatalog.DictionaryId.ToString(), (await search.SearchAsync("学习")).Items[0].SourceId);
        Assert.Equal(0, Assert.Single(await new ResourceManagementStore(new(folder.DatabasePath)).GetAsync(ResourceKind.Dictionary)).State.Priority);
        var error = await Assert.ThrowsAsync<PackageException>(() => management.SetPriorityAsync(dictionary, 1)); Assert.Equal("PLAN_STALE", error.Code);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => management.SetPriorityAsync(dictionary, -1));
    }

    [Fact]
    public async Task BundledUninstallSurvivesStartupAndExplicitRestorePreservesPriorityAndOverrides()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); await Seed(db);
        var management = new ResourceManagementStore(db); var states = new ResourceStateStore(db);
        var dictionary = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        await management.SetPriorityAsync(dictionary, 77); dictionary = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        var entry = (await states.GetEntriesAsync(dictionary.State.ResourceId))[0];
        await states.SetEntryRemovedAsync(dictionary.State.ResourceId, entry.EntryId, true, dictionary.State.RowRevision, DateTimeOffset.UtcNow,
            new(Guid.NewGuid(), dictionary.State.ResourceId, ResourceOperationType.Remove, dictionary.Epoch, DateTimeOffset.UtcNow, "{}"));
        dictionary = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        var plan = await management.PreviewUninstallAsync(dictionary); Assert.True(plan.CanUninstall);
        await management.UninstallAsync(plan);
        var catalog = new BundledResourceCatalog(new(folder.DatabasePath), new(new(folder.DatabasePath))); await catalog.EnsureInstalledAsync();
        var missing = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        Assert.False(missing.State.IsPresent); Assert.False(missing.State.IsEnabled); Assert.Equal(0, missing.Count); Assert.Equal(1, missing.RemovedCount);
        Assert.Empty(await states.GetEntriesAsync(dictionary.State.ResourceId));
        Assert.DoesNotContain(await new OfflineSearchStore(db).GetSourcesAsync(), s => s.Id == dictionary.State.ResourceId.ToString());
        await catalog.RestoreAsync(dictionary.State.ResourceId);
        var restored = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        Assert.True(restored.State.IsPresent); Assert.True(restored.State.IsEnabled); Assert.Equal(3, restored.Count);
        Assert.Equal(77, restored.State.Priority); Assert.Equal(1, restored.RemovedCount);
        Assert.True(restored.State.RowRevision > missing.State.RowRevision);
        Assert.DoesNotContain(entry.ContentId, await states.GetQueryableContentIdsAsync(ResourceKind.Dictionary));
        Assert.Contains(await states.GetEntriesAsync(dictionary.State.ResourceId), e => e.ContentId == entry.ContentId);
        await Assert.ThrowsAsync<PackageException>(() => management.UninstallAsync(plan));
    }

    [Fact]
    public async Task ExternalSamePackageReinstallsWithStableIdentityAfterUninstall()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); var installer = new TextResourceInstaller(db);
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-dictionary.handict"));
        var install = await installer.PlanAsync(input); await installer.InstallAsync(install);
        var management = new ResourceManagementStore(db); var resource = Assert.Single(await management.GetAsync());
        var entries = await new ResourceStateStore(db).GetEntriesAsync(resource.State.ResourceId);
        await management.UninstallAsync(await management.PreviewUninstallAsync(resource));
        input.Position = 0; var reinstall = await installer.PlanAsync(input); Assert.False((await installer.InstallAsync(reinstall)).AlreadyInstalled);
        Assert.Equal(entries, await new ResourceStateStore(db).GetEntriesAsync(resource.State.ResourceId));
        Assert.True((await installer.InstallAsync(reinstall)).AlreadyInstalled);
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("audio")]
    [InlineData("draftTarget")]
    [InlineData("draftToken")]
    [InlineData("teaching")]
    [InlineData("modified")]
    [InlineData("retained")]
    public async Task ReferencedOrModifiedContentCannotBeUninstalled(string protection)
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); await Seed(db);
        var management = new ResourceManagementStore(db); var resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        var ids = await Ids(db, resource.State.ResourceId);
        var planBeforeReference = await management.PreviewUninstallAsync(resource); Assert.True(planBeforeReference.CanUninstall);
        if (protection == "draftToken") ids["$token"] = ids["$token"].ToUpperInvariant();
        var sql = protection switch
        {
            "favorite" => "INSERT INTO favorite_folder(id,name,name_key) VALUES('folder','Test','test'); INSERT INTO favorite_item VALUES('folder',$content,0,'2026-09-17T00:00:00Z');",
            "audio" => "INSERT INTO audio_asset VALUES('asset',printf('%064d',0),'user.wav',100,100,'wav','pcm',16000,1,'user'); INSERT INTO audio_binding VALUES('binding',$unit,'asset',printf('%064d',0),printf('%064d',0),'confirmed','user','');",
            "draftTarget" => "INSERT INTO draft VALUES('draft',$content,'content',1,'{}','2026-09-17T00:00:00Z');",
            "draftToken" => "INSERT INTO draft VALUES('draft',NULL,'content',1,json_object('nested',json_object('tokenId',$token)),'2026-09-17T00:00:00Z');",
            "teaching" => "INSERT INTO pinyin_item VALUES('pinyin','test','test',0,json_object('example',json_object('unitId',$unit)));",
            "modified" => "UPDATE content SET row_revision=row_revision+1 WHERE id=$content;",
            _ => "INSERT INTO retained_content VALUES($content,$resource,'entry','1.0.0','explicitKeep','2026-09-17T00:00:00Z');"
        };
        await Execute(db, sql, ids);
        var protectedPlan = await management.PreviewUninstallAsync(resource); Assert.False(protectedPlan.CanUninstall);
        var error = await Assert.ThrowsAsync<PackageException>(() => management.UninstallAsync(planBeforeReference));
        Assert.Equal("RESOURCE_DEPENDENCIES_EXIST", error.Code); Assert.True(Assert.Single(await management.GetAsync(ResourceKind.Dictionary)).State.IsPresent);
        Assert.Equal(3, Assert.Single(await management.GetAsync(ResourceKind.Dictionary)).Count);
    }

    [Fact]
    public async Task FailedUninstallOrPriorityReceiptRollsBackEntireOperation()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); await Seed(db);
        var management = new ResourceManagementStore(db); var before = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        var plan = await management.PreviewUninstallAsync(before);
        await Execute(db, "CREATE TRIGGER fail_operation BEFORE INSERT ON resource_operation BEGIN SELECT RAISE(ABORT,'injected'); END;");
        await Assert.ThrowsAnyAsync<Exception>(() => management.UninstallAsync(plan));
        await Assert.ThrowsAnyAsync<Exception>(() => management.SetPriorityAsync(before, 999));
        Assert.Equal(before, Assert.Single(await management.GetAsync(ResourceKind.Dictionary)));
        Assert.Equal(3, (await new ResourceStateStore(db).GetEntriesAsync(before.State.ResourceId)).Count);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => management.UninstallAsync(plan, cancellation.Token));
        Assert.Equal(before, Assert.Single(await management.GetAsync(ResourceKind.Dictionary)));
    }

    [Fact]
    public async Task IncompleteResourceIsBlockedAndStalePreviewCannotCommit()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); await Seed(db);
        var management = new ResourceManagementStore(db); var resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        var plan = await management.PreviewUninstallAsync(resource);
        await management.SetPriorityAsync(resource, 90);
        var error = await Assert.ThrowsAsync<PackageException>(() => management.UninstallAsync(plan)); Assert.Equal("PLAN_STALE", error.Code);
        var ids = await Ids(db, resource.State.ResourceId);
        await Execute(db, "DELETE FROM resource_entry WHERE content_id=$content;", ids);
        resource = Assert.Single(await management.GetAsync(ResourceKind.Dictionary));
        Assert.False((await management.PreviewUninstallAsync(resource)).CanUninstall);
    }

    private static Task Seed(HanMateDatabase db) => new BundledResourceCatalog(db, new(db)).EnsureInstalledAsync();
    private static async Task<Dictionary<string, string>> Ids(HanMateDatabase db, Guid resource)
    {
        var entry = (await new ResourceStateStore(db).GetEntriesAsync(resource))[0];
        var document = (await new SqliteContentDocumentStore(db, new()).GetAsync(entry.ContentId))!.Document;
        return new() { ["$resource"] = resource.ToString(), ["$content"] = entry.ContentId.ToString(),
            ["$unit"] = document.TextUnits[0].Id.ToString(), ["$token"] = document.TextUnits[0].Tokens[0].Id.ToString() };
    }
    private static async Task Execute(HanMateDatabase db, string sql, Dictionary<string,string>? parameters = null)
    {
        using var connection = await db.OpenConnectionAsync(); using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var pair in parameters ?? []) command.Parameters.AddWithValue(pair.Key, pair.Value);
        await command.ExecuteNonQueryAsync();
    }
}
