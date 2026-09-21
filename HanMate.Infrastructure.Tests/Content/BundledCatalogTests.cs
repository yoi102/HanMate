using HanMate.Core.Content;
using HanMate.Core.Audio;
using System.Text.Json;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;
using HanMate.Infrastructure.Tests.Database;

namespace HanMate.Infrastructure.Tests.Content;

public sealed class BundledCatalogTests
{
    private static async Task<ResourceLibraryItem[]> ReadLibraryAsync(TextResourceInstaller installer, ContentKind? kind = null, ResourceKind? resourceKind = null)
    {
        var result = new List<ResourceLibraryItem>();
        for (var offset = 0; ; offset += 50)
        {
            var page = await installer.GetLibraryAsync(offset: offset, kind: kind, resourceKind: resourceKind);
            result.AddRange(page);
            if (page.Count < 50) return result.ToArray();
        }
    }

    [Theory]
    [InlineData("1.0.2")]
    [InlineData("1.0.3")]
    [InlineData("1.0.4")]
    public async Task CategoryUpgradePreservesExistingUnitsAndFavoriteAudio(string previousVersion)
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath);
        var installer = new TextResourceInstaller(db);
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", $"learning-v{previousVersion}.zip"));
        var package = await TextResourcePackageReader.ReadAsync(stream, CancellationToken.None);
        await installer.InstallAsync(await installer.PlanPackageAsync(package with
        { Registration = package.Registration with { Distribution = ResourceDistribution.Bundled } }));
        var old = package.Contents;
        var doc = old.First(d => d.Kind == ContentKind.Word);
        var favorites = new FavoriteStore(db);
        var selection = await favorites.GetSelectionAsync(doc.Id);
        await favorites.SetSelectionAsync(doc.Id, [FavoriteStore.DefaultId], selection.Revision);
        var audio = new LocalAudioStore(db); var target = doc.TextUnits.Single(u => u.Role == TextUnitRole.Headword).Id;
        using var wav = new MemoryStream(); wav.SetLength(16044); PcmWave.WriteHeader(wav, 8000, 1, 16000);
        var capture = await audio.BeginAsync(await audio.GetTargetAsync(target), "user");
        await File.WriteAllBytesAsync(audio.CapturePath(capture.Id), wav.ToArray());
        await audio.SaveAsync(await audio.FinalizeAsync(capture.Id), "test", true);
        await new BundledResourceCatalog(db, installer).EnsureInstalledAsync();
        var store = new SqliteContentDocumentStore(db, new());
        foreach (var previous in old)
        {
            var current = (await store.GetAsync(previous.Id))!.Document;
            Assert.Equal(JsonSerializer.Serialize(previous with { Source = previous.Source with { ResourceVersion = BundledResourceCatalog.LearningVersion } }, ContentJson.Options),
                JsonSerializer.Serialize(current, ContentJson.Options));
        }
        Assert.Equal(doc.Id, Assert.Single(await favorites.GetEntriesAsync(FavoriteStore.DefaultId)).Id);
        Assert.Empty(await new ResourceManagementStore(db).GetRetainedAsync());
        await using (var playback = await audio.OpenPlaybackAsync("target", target))
        { using var bytes = new MemoryStream(); await playback.Stream.CopyToAsync(bytes); Assert.Equal(wav.ToArray(), bytes.ToArray()); }
        Assert.Equal(124, (await new LearningCatalogStore(db).GetWordCategoryCountsAsync()).Total);
        Assert.Equal(BundledResourceCatalog.LearningVersion, (await new ResourceStateStore(db).GetAsync(BundledResourceCatalog.LearningId))!.Version);
    }

    private static async Task SeedV1(TextResourceInstaller installer)
    {
        foreach (var name in new[] { "learning", "dictionary" })
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Catalog", name + "-v1.zip"));
            var package = await TextResourcePackageReader.ReadAsync(stream, CancellationToken.None);
            await installer.InstallAsync(await installer.PlanPackageAsync(package with {
                Registration = package.Registration with { Distribution = ResourceDistribution.Bundled } }));
        }
    }

    private static async Task<object?> Sql(HanMateDatabase db, string sql, params (string, object)[] parameters)
    {
        using var c = await db.OpenConnectionAsync(); using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value);
        return await cmd.ExecuteScalarAsync();
    }

    [Theory]
    [InlineData(ResourceKind.Dictionary)]
    [InlineData(ResourceKind.Learning)]
    public async Task V1UpgradePreservesFavoriteRecordingDraftAndResourcePreferences(ResourceKind resourceKind)
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath);
        var installer = new TextResourceInstaller(db); await SeedV1(installer);
        var doc = JsonSerializer.Deserialize<ContentDocument>((await installer.GetLibraryAsync(kind: ContentKind.Word, resourceKind: resourceKind)).First().BodyJson, ContentJson.Options)!;
        await Sql(db, "INSERT INTO favorite_folder(id,name,name_key) VALUES('test','test','test'); INSERT INTO favorite_item VALUES('test',$id,7,'2026-09-19T00:00:00Z')", ("$id", doc.Id.ToString()));
        var audio = new LocalAudioStore(db); var target = await audio.GetTargetAsync(doc.TextUnits[0].Id);
        using var wav = new MemoryStream(); wav.SetLength(16044); PcmWave.WriteHeader(wav, 8000, 1, 16000);
        var capture = await audio.BeginAsync(target, "user"); await File.WriteAllBytesAsync(audio.CapturePath(capture.Id), wav.ToArray());
        await audio.SaveAsync(await audio.FinalizeAsync(capture.Id), "test", true);
        var draft = await audio.BeginAsync(target, "user"); await File.WriteAllBytesAsync(audio.CapturePath(draft.Id), wav.ToArray());
        await audio.FinalizeAsync(draft.Id);
        await Sql(db, "UPDATE installed_resource SET enabled=0,priority=37 WHERE resource_id=$id; INSERT INTO resource_entry_override(resource_id,entry_id,removed,updated_at_utc) VALUES($id,$entry,1,'2026-09-19T00:00:00Z')",
            ("$id", doc.Source.ResourceId!.Value.ToString()), ("$entry", doc.Source.EntryId!));
        var catalog = new BundledResourceCatalog(db, installer); await catalog.EnsureInstalledAsync();
        var state = (await new ResourceStateStore(db).GetAsync(doc.Source.ResourceId!.Value))!;
        Assert.Equal(resourceKind == ResourceKind.Learning ? BundledResourceCatalog.LearningVersion : BundledResourceCatalog.Version, state.Version);
        Assert.False(state.IsEnabled); Assert.Equal(37, state.Priority);
        Assert.Equal(1L, await Sql(db, "SELECT removed FROM resource_entry_override WHERE entry_id=$entry", ("$entry", doc.Source.EntryId!)));
        var retained = JsonSerializer.Deserialize<ContentDocument>(Assert.Single(await new ResourceManagementStore(db).GetRetainedAsync()).BodyJson, ContentJson.Options)!;
        Assert.NotEqual(doc.Id, retained.Id); Assert.Equal("1.0.0", retained.Source.ResourceVersion);
        Assert.Equal(retained.Id.ToString(), await Sql(db, "SELECT content_id FROM favorite_item WHERE folder_id='test' AND sort_order=7"));
        Assert.Equal(draft.Id, Assert.Single(await audio.ListDraftsAsync(retained.TextUnits[0].Id)).Id);
        await using (var playback = await audio.OpenPlaybackAsync("target", retained.TextUnits[0].Id))
        { using var bytes = new MemoryStream(); await playback.Stream.CopyToAsync(bytes); Assert.Equal(wav.ToArray(), bytes.ToArray()); }
        Assert.Empty(await audio.ListTracksAsync(doc.TextUnits[0].Id));
        var current = (string)(await Sql(db, "SELECT body_json FROM content WHERE id=$id", ("$id", doc.Id.ToString())))!;
        Assert.Contains("definition", current);
        var epoch = await Sql(db, "SELECT data_epoch FROM app_state"); await catalog.EnsureInstalledAsync();
        Assert.Equal(epoch, await Sql(db, "SELECT data_epoch FROM app_state"));
    }

    [Fact]
    public async Task PendingRecordingDefersOnlyItsCatalogUntilReady()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath);
        var installer = new TextResourceInstaller(db); await SeedV1(installer);
        var doc = JsonSerializer.Deserialize<ContentDocument>((await installer.GetLibraryAsync(resourceKind: ResourceKind.Dictionary)).First().BodyJson, ContentJson.Options)!;
        var audio = new LocalAudioStore(db); var draft = await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id), "user");
        var catalog = new BundledResourceCatalog(db, installer); await catalog.EnsureInstalledAsync();
        Assert.Equal("1.0.0", (await new ResourceStateStore(db).GetAsync(BundledResourceCatalog.DictionaryId))!.Version);
        Assert.Equal(BundledResourceCatalog.LearningVersion, (await new ResourceStateStore(db).GetAsync(BundledResourceCatalog.LearningId))!.Version);
        using var wav = new MemoryStream(); wav.SetLength(16044); PcmWave.WriteHeader(wav, 8000, 1, 16000);
        await File.WriteAllBytesAsync(audio.CapturePath(draft.Id), wav.ToArray()); await audio.FinalizeAsync(draft.Id);
        await catalog.EnsureInstalledAsync();
        Assert.Equal(BundledResourceCatalog.Version, (await new ResourceStateStore(db).GetAsync(BundledResourceCatalog.DictionaryId))!.Version);
    }

    [Fact]
    public async Task StartupDoesNotDowngradeNewerCatalog()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath);
        var installer = new TextResourceInstaller(db); var catalog = new BundledResourceCatalog(db, installer);
        await catalog.EnsureInstalledAsync();
        await Sql(db, "UPDATE installed_resource SET version='10.0.0'");
        var epoch = await Sql(db, "SELECT data_epoch FROM app_state");
        await catalog.EnsureInstalledAsync();
        Assert.Equal("10.0.0", (await new ResourceStateStore(db).GetAsync(BundledResourceCatalog.DictionaryId))!.Version);
        Assert.Equal(epoch, await Sql(db, "SELECT data_epoch FROM app_state"));
    }

    [Fact]
    public async Task RemovedV1StaysRemovedUntilExplicitRestore()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath);
        var installer = new TextResourceInstaller(db); await SeedV1(installer);
        var management = new ResourceManagementStore(db);
        var resource = (await management.GetAsync()).Single(r => r.State.ResourceId == BundledResourceCatalog.DictionaryId);
        await management.UninstallRetainingAsync(await management.PreviewRetainingUninstallAsync(resource));
        var catalog = new BundledResourceCatalog(db, installer); await catalog.EnsureInstalledAsync();
        Assert.False((await new ResourceStateStore(db).GetAsync(resource.State.ResourceId))!.IsPresent);
        await catalog.RestoreAsync(resource.State.ResourceId);
        Assert.Equal(BundledResourceCatalog.Version, (await new ResourceStateStore(db).GetAsync(resource.State.ResourceId))!.Version);
    }

    [Fact]
    public async Task ExternalNewPayloadCannotUpdateReservedV1()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath);
        var installer = new TextResourceInstaller(db); await SeedV1(installer);
        using var stream = typeof(BundledResourceCatalog).Assembly.GetManifestResourceStream("Starter.learning.zip")!;
        Assert.Equal("RESOURCE_ID_RESERVED", (await Assert.ThrowsAsync<PackageException>(() => installer.PlanAsync(stream))).Code);
        Assert.Equal("1.0.0", (await new ResourceStateStore(db).GetAsync(BundledResourceCatalog.LearningId))!.Version);
    }

    [Fact]
    public async Task StarterInstallsFourKindsAndDictionaryOnceAndSurvivesReopen()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); var installer = new TextResourceInstaller(db);
        await new BundledResourceCatalog(db, installer).EnsureInstalledAsync();
        await new BundledResourceCatalog(new(folder.DatabasePath), new(new(folder.DatabasePath))).EnsureInstalledAsync();
        var library = await ReadLibraryAsync(installer);
        Assert.Equal(144, library.Length); Assert.Equal(144, library.Select(x => x.Id).Distinct().Count());
        Assert.Equal(4, library.Select(r => r.Kind).Distinct().Count());
        Assert.Equal(7, (await installer.GetLibraryAsync(kind: ContentKind.Poem)).Count);
        Assert.Equal(7, (await installer.GetLibraryAsync(kind: ContentKind.Text)).Count);
        Assert.Equal(124, (await ReadLibraryAsync(installer, ContentKind.Word, ResourceKind.Learning)).Length);
        Assert.Equal(3, (await installer.GetLibraryAsync(resourceKind: ResourceKind.Dictionary)).Count);
        Assert.Equal(ResourceDistribution.Bundled, (await new ResourceStateStore(db).GetAsync(BundledResourceCatalog.LearningId))!.Distribution);
    }

    [Fact]
    public async Task StartupDoesNotReactivateDisabledOrMissingResources()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); var installer = new TextResourceInstaller(db);
        var catalog = new BundledResourceCatalog(db, installer); await catalog.EnsureInstalledAsync();
        using (var c = await db.OpenConnectionAsync())
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "UPDATE installed_resource SET enabled=0,is_present=0;"; await cmd.ExecuteNonQueryAsync(); }
        await catalog.EnsureInstalledAsync(); Assert.Empty(await installer.GetLibraryAsync());
    }

    [Fact]
    public async Task ExternalInputCannotReserveOrPromoteBundledIdentity()
    {
        using var folder = new TestDatabaseDirectory(); var db = new HanMateDatabase(folder.DatabasePath); var installer = new TextResourceInstaller(db);
        using var input = typeof(BundledResourceCatalog).Assembly.GetManifestResourceStream("Starter.learning.zip")!;
        var plan = await installer.PlanAsync(input);
        var error = await Assert.ThrowsAsync<PackageException>(() => installer.InstallAsync(plan));
        Assert.Equal("RESOURCE_ID_RESERVED", error.Code);
        Assert.Empty(await installer.GetLibraryAsync());
        await new BundledResourceCatalog(db, installer).EnsureInstalledAsync();
        Assert.Equal(144, (await ReadLibraryAsync(installer)).Length);
    }
}
