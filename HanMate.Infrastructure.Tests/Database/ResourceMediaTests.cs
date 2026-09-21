using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Tests.Database;

public sealed partial class BackupTests
{
    [Fact]
    public async Task SystemSpeechIsOptInAndBackupSettingsRespectApplyChoice()
    {
        using var a = new Area(); await a.Seed(); var policy = new SpeechPolicyStore(new VersionedLocalStateStore(a.Db));
        Assert.False(await policy.IsAllowedAsync()); await policy.SetAllowedAsync(true); Assert.True(await policy.IsAllowedAsync());
        using var export = await a.Export(); using var b = new Area(); var importer = new BackupImportStore(b.Db);
        await importer.CommitAsync(await importer.PlanAsync(export));
        var local = new SpeechPolicyStore(new VersionedLocalStateStore(b.Db)); Assert.False(await local.IsAllowedAsync());
        export.Position = 0; await importer.CommitAsync(await importer.PlanAsync(export, true)); Assert.True(await local.IsAllowedAsync());
        await local.SetAllowedAsync(false); Assert.False(await local.IsAllowedAsync());
    }
    [Fact]
    public async Task SuccessiveTeachingImportsRespectBackupCapacityAndAllowExactReimport()
    {
        using var b = new Area(); using var package = ResourceWave(); var installer = new TextResourceInstaller(b.Db);
        await installer.InstallAsync(await installer.PlanAsync(package));
        var document = await ResourceDocument(b); var store = new TeachingCatalogStore(b.Db);
        using var seed = Teaching(document); var catalog = JsonNode.Parse(seed.ToArray())!;
        var first = catalog["items"]![0]!.DeepClone();
        var items = catalog["items"]!.AsArray();
        for (var i = 1; i < 199; i++) { var item = first.DeepClone(); item["id"] = Guid.NewGuid().ToString(); items.Add(item); }
        using var batch = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(catalog));
        await store.CommitAsync(await store.PlanAsync(batch));
        using var last = Teaching(document); await store.CommitAsync(await store.PlanAsync(last));
        Assert.Equal(200L, await b.Scalar("SELECT count(*) FROM pinyin_item"));
        last.Position = 0; await store.CommitAsync(await store.PlanAsync(last));
        using var excess = Teaching(document);
        Assert.Equal("PACKAGE_LIMIT_EXCEEDED", (await Assert.ThrowsAsync<PackageException>(() => store.PlanAsync(excess))).Code);
        Assert.Equal(200L, await b.Scalar("SELECT count(*) FROM pinyin_item"));
        using var exported = await b.Export();
        using var target = new Area(); var restore = new BackupImportStore(target.Db);
        await restore.CommitAsync(await restore.PlanAsync(exported));
        // Backup merge collapses semantically identical rows; the resulting course remains usable.
        Assert.Single((await new TeachingCatalogStore(target.Db).LoadAsync(new PinyinCourse(new("empty", [], [], [])))).Data.Items);
    }

    [Fact]
    public async Task ConcurrentTeachingPlansCannotOverflowOrPartiallyCommit()
    {
        using var b = new Area(); using var package = ResourceWave(); var installer = new TextResourceInstaller(b.Db);
        await installer.InstallAsync(await installer.PlanAsync(package));
        var document = await ResourceDocument(b); var store = new TeachingCatalogStore(b.Db);
        using var first = Teaching(document); using var second = Teaching(document);
        var a = await store.PlanAsync(first); var c = await store.PlanAsync(second);
        await store.CommitAsync(a);
        Assert.Equal("PLAN_STALE", (await Assert.ThrowsAsync<PackageException>(() => store.CommitAsync(c))).Code);
        Assert.Equal(1L, await b.Scalar("SELECT count(*) FROM pinyin_item"));
    }
    [Fact]
    public async Task ResourceWaveInstallsPlaysUninstallsAndReattachesWithoutLosingPersonalAudio()
    {
        using var b=new Area(); using var package=ResourceWave(); var installer=new TextResourceInstaller(b.Db);
        await installer.InstallAsync(await installer.PlanAsync(package));
        var d=await ResourceDocument(b); await b.Record(d); var audio=new LocalAudioStore(b.Db);
        Assert.Equal(2,(await audio.ListTracksAsync(d.TextUnits[0].Id)).Count);
        var manager=new ResourceManagementStore(b.Db); var plan=await manager.PreviewRetainingUninstallAsync(Assert.Single(await manager.GetAsync()));
        Assert.True(plan.RetainedCount>0); await manager.UninstallRetainingAsync(plan);
        await using(var lease=await audio.OpenPlaybackAsync("target",d.TextUnits[0].Id)) Assert.Equal(1000,PcmWave.Inspect(lease.Stream).DurationMs);
        package.Position=0; await installer.InstallAsync(await installer.PlanAsync(package));
        Assert.Equal(2,(await audio.ListTracksAsync(d.TextUnits[0].Id)).Count); Assert.Empty(await manager.GetRetainedAsync());
        var backup=await new BackupExportStore(b.Db).PreviewAsync(); Assert.Equal(1,backup.IncludedResources); Assert.Empty(backup.MissingResources);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task ResourceAudioUpdateRetainsOldGraphsTeachingAndDefaults(bool uninstallFirst)
    {
        using var b=new Area(); using var old=ResourceWave(); var installer=new TextResourceInstaller(b.Db); await installer.InstallAsync(await installer.PlanAsync(old));
        var d=await ResourceDocument(b); var teaching=new TeachingCatalogStore(b.Db);
        using(var catalog=Teaching(d)) await teaching.CommitAsync(await teaching.PlanAsync(catalog));
        var favorites=new FavoriteStore(b.Db); var folder=(await favorites.GetFoldersAsync())[0];
        await favorites.SetSelectionAsync(d.Id,[folder.Id],(await favorites.GetSelectionAsync(d.Id)).Revision);
        var manager=new ResourceManagementStore(b.Db);
        if(uninstallFirst) await manager.UninstallRetainingAsync(await manager.PreviewRetainingUninstallAsync(Assert.Single(await manager.GetAsync())));
        using var next=ResourceWave("2.0.0"); var plan=await installer.PlanAsync(next); Assert.True(plan.Update!.CanApply); Assert.True(plan.Update.Retained>0);
        await installer.InstallAsync(plan);
        var retained=JsonSerializer.Deserialize<ContentDocument>(Assert.Single(await manager.GetRetainedAsync()).BodyJson,ContentJson.Options)!;
        Assert.NotEqual(d.Id,retained.Id); Assert.Equal(retained.Id.ToString(),await b.Scalar("SELECT content_id FROM favorite_item"));
        var course=await teaching.LoadAsync(new PinyinCourse(new("empty",[],[],[]))); var example=Assert.Single(Assert.Single(course.Data.Items).Examples);
        Assert.Equal(retained.Id,example.ContentId); Assert.StartsWith("local:checked:",course.PlaybackKey(example));
        await using(var lease=await new LocalAudioStore(b.Db).OpenPlaybackAsync("target",retained.TextUnits[0].Id)) Assert.Equal(1000,PcmWave.Inspect(lease.Stream).DurationMs);
        await using(var lease=await new LocalAudioStore(b.Db).OpenPlaybackAsync("target",d.TextUnits[0].Id)) Assert.Equal(1000,PcmWave.Inspect(lease.Stream).DurationMs);
        using var export=await b.Export(); using var dest=new Area(); var backup=new BackupImportStore(dest.Db); await backup.CommitAsync(await backup.PlanAsync(export));
        Assert.Equal(1,(await new BackupExportStore(dest.Db).PreviewAsync()).IncludedResources);
        Assert.Equal(1L,await dest.Scalar("SELECT count(*) FROM pinyin_item"));
        Assert.Single((await new TeachingCatalogStore(dest.Db).LoadAsync(new PinyinCourse(new("empty",[],[],[])))).Data.Items);
    }
    [Fact]
    public async Task TeachingCatalogValidatesHighlightAndFollowsEnabledRemovedState()
    {
        using var b=new Area(); using var input=ResourceWave(); var installer=new TextResourceInstaller(b.Db); await installer.InstallAsync(await installer.PlanAsync(input));
        var d=await ResourceDocument(b); var store=new TeachingCatalogStore(b.Db); using var catalog=Teaching(d); var plan=await store.PlanAsync(catalog); await store.CommitAsync(plan);
        var empty=new PinyinCourse(new("empty",[],[],[])); Assert.Single((await store.LoadAsync(empty)).Data.Items);
        await b.Execute("UPDATE installed_resource SET enabled=0"); Assert.Empty((await store.LoadAsync(empty)).Data.Items);
        await b.Execute("UPDATE installed_resource SET enabled=1"); Assert.Single((await store.LoadAsync(empty)).Data.Items);
        using var bad=Teaching(d,true); await Assert.ThrowsAnyAsync<Exception>(()=>store.PlanAsync(bad)); Assert.Equal(1L,await b.Scalar("SELECT count(*) FROM pinyin_item"));
    }
    [Fact]
    public async Task CorruptResourceMediaAndFailedUpdateDoNotChangeOldResource()
    {
        using var b=new Area(); using var input=ResourceWave(); var installer=new TextResourceInstaller(b.Db); await installer.InstallAsync(await installer.PlanAsync(input));
        using var next=ResourceWave("2.0.0"); var plan=await installer.PlanAsync(next);
        await b.Execute("CREATE TRIGGER fail_update BEFORE INSERT ON resource_operation BEGIN SELECT RAISE(ABORT,'disk failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(()=>installer.InstallAsync(plan)); Assert.Equal("1.0.0",await b.Scalar("SELECT version FROM installed_resource"));
        Assert.Equal(1L,await b.Scalar("SELECT count(*) FROM audio_binding"));
        using var corrupt=Rewrite(next,files=> { var path=files.Keys.Single(p=>p.EndsWith(".wav")); files[path][^1]^=1; });
        await Assert.ThrowsAsync<PackageException>(()=>installer.PlanAsync(corrupt));
    }
    private static async Task<ContentDocument> ResourceDocument(Area b) => JsonSerializer.Deserialize<ContentDocument>((string)(await b.Scalar("SELECT c.body_json FROM content c JOIN playback_target t ON t.content_id=c.id JOIN audio_binding b ON b.target_id=t.id WHERE b.source_role='standard' LIMIT 1"))!,ContentJson.Options)!;
    private static MemoryStream Teaching(ContentDocument d,bool bad=false)
    {
        var unit=d.TextUnits.First(u=>u.Tokens.Any(t=>t.Pinyin is not null)); var token=unit.Tokens.First(t=>t.Pinyin is not null); var examples=new JsonArray();
        for(var tone=1;tone<=4;tone++) examples.Add(new JsonObject { ["tone"]=tone,["status"]=tone==token.Pinyin!.Tone ? "available":"unavailable",
            ["contentId"]=tone==token.Pinyin.Tone ? d.Id.ToString():null,["note"]="fixture",["highlight"]=tone==token.Pinyin.Tone ? new JsonObject {
                ["contentId"]=d.Id.ToString(),["unitId"]=unit.Id.ToString(),["tokenId"]=token.Id.ToString(),["pinyinElementStart"]=0,["pinyinElementLength"]=bad ? 999:1 }:null });
        var item=new JsonObject { ["id"]=Guid.NewGuid().ToString(),["groupCode"]="initial",["display"]=token.Pinyin!.Base[..1],["sortOrder"]=0,["demoAudioAssetId"]=null,["reviewStatus"]="draft",["examples"]=examples };
        return new(JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["schemaVersion"]=1,["catalogId"]=Guid.NewGuid().ToString(),["catalogVersion"]="test",["items"]=new JsonArray(item) }));
    }
    private static MemoryStream ResourceWave(string version="1.0.0")
    {
        using var original=new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures/sample-learning.hanresource")));
        return Rewrite(original,files=> {
            var contents=JsonNode.Parse(files["contents.json"])!; var descriptor=JsonNode.Parse(files["resource.json"])!; var manifest=JsonNode.Parse(files["manifest.json"])!;
            foreach(var doc in contents["contents"]!.AsArray()) { doc!["source"]!["resourceVersion"]=version; if(version!="1.0.0") doc["title"]="Updated "+doc["title"]!.GetValue<string>(); }
            descriptor["version"]=version; var first=contents["contents"]![0]!; var unit=first["textUnits"]![0]!;
            var text=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(unit["text"]!.GetValue<string>())));
            var pronunciation=Hash(new JsonArray(unit["tokens"]!.AsArray().Select(t=>(JsonNode)new JsonArray(t!["start"]!.DeepClone(),t["length"]!.DeepClone(),t["pinyin"]?["base"]?.DeepClone(),t["pinyin"]?["tone"]?.DeepClone(),t["pinyin"]?["erhua"]?.DeepClone())).ToArray()));
            const string asset="835246e0-d965-46aa-9ba5-becfd287ab2c",binding="5a20e667-8546-46a9-82d3-6229497b75ad";
            var bytes=Wave(); var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)); var path="audio/"+hash+".wav";
            var defaults=new JsonArray(new JsonObject { ["targetId"]=unit["id"]!.DeepClone(),["bindingId"]=binding });
            var audio=new JsonObject { ["schemaVersion"]=1,["assets"]=new JsonArray(new JsonObject { ["id"]=asset,["origin"]="catalog",["storage"]="package",["sha256"]=hash,["byteLength"]=bytes.Length,["durationMs"]=1000,["container"]="wav",["codec"]="pcm_s16le",["sampleRate"]=8000,["channels"]=1,["path"]=path,["catalogRef"]=null }),
                ["bindings"]=new JsonArray(new JsonObject { ["id"]=binding,["targetId"]=unit["id"]!.DeepClone(),["assetId"]=asset,["boundTextHash"]=text,["boundPronunciationHash"]=pronunciation,["reviewState"]="confirmed",["sourceRole"]="standard",["label"]="synthetic" }),["preferences"]=defaults };
            descriptor["audioBindingIds"]=new JsonArray(binding); descriptor["audioDefaults"]=defaults.DeepClone();
            manifest["counts"]!["audioAssets"]=1; manifest["counts"]!["audioBindings"]=1;
            files["manifest.json"]=JsonSerializer.SerializeToUtf8Bytes(manifest); files["contents.json"]=JsonSerializer.SerializeToUtf8Bytes(contents);
            files["resource.json"]=JsonSerializer.SerializeToUtf8Bytes(descriptor); files["audio/index.json"]=JsonSerializer.SerializeToUtf8Bytes(audio); files[path]=bytes;
        });
    }
}
