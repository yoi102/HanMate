using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Tests.Database;

public sealed class ResourceUpdateTests
{
    [Fact]
    public async Task ReadyAudioDraftMovesWithOldGraphAndCanBeSavedAfterReload()
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0]; var audio = new LocalAudioStore(area.Db);
        var pending = await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id), "user");
        await File.WriteAllBytesAsync(audio.CapturePath(pending.Id), Wave()); var ready = await audio.FinalizeAsync(pending.Id);
        var before = await Scalar(area, "SELECT updated_at_utc FROM draft WHERE id=$id", ("$id", ready.Id.ToString()));
        using var input = Package("all-change"); var plan = await area.Installer.PlanAsync(input);
        Assert.True(plan.Update!.CanApply); Assert.Equal(1, plan.Update.AudioDrafts); Assert.Equal(1, plan.Update.Retained);
        await area.Installer.InstallAsync(plan);
        Assert.Empty(await audio.ListDraftsAsync(doc.TextUnits[0].Id));
        var snapshot = JsonSerializer.Deserialize<ContentDocument>(Assert.Single(await new ResourceManagementStore(area.Db).GetRetainedAsync()).BodyJson, ContentJson.Options)!;
        var moved = Assert.Single(await audio.ListDraftsAsync(snapshot.TextUnits[0].Id));
        Assert.Equal(ready.Id,moved.Id); Assert.Equal(ready.Sha256,moved.Sha256); Assert.Equal(ready.Wave,moved.Wave);
        Assert.Equal(ready.Target.TextHash,moved.Target.TextHash); Assert.Equal(ready.Target.PronunciationHash,moved.Target.PronunciationHash);
        Assert.Equal(before,await Scalar(area,"SELECT updated_at_utc FROM draft WHERE id=$id",("$id",ready.Id.ToString())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => audio.SaveAsync(ready,"outdated",true));
        await audio.SaveAsync(moved,"retained draft",true);
        await using var lease = await audio.OpenPlaybackAsync("target",snapshot.TextUnits[0].Id);
        using var bytes = new MemoryStream(); await lease.Stream.CopyToAsync(bytes); Assert.Equal(Wave(),bytes.ToArray());
        Assert.Empty(await audio.ListTracksAsync(doc.TextUnits[0].Id));
    }

    [Fact]
    public async Task RelocationDoesNotConfirmStaleDraftPronunciation()
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0]; var audio = new LocalAudioStore(area.Db);
        var draft = await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id),"user"); await File.WriteAllBytesAsync(audio.CapturePath(draft.Id),Wave()); await audio.FinalizeAsync(draft.Id);
        await Execute(area,"UPDATE draft SET body_json=json_set(body_json,'$.Draft.Target.PronunciationHash',printf('%064d',0)) WHERE id=$id",("$id",draft.Id.ToString()));
        using var input = Package("all-change"); await area.Installer.InstallAsync(await area.Installer.PlanAsync(input));
        var snapshot = JsonSerializer.Deserialize<ContentDocument>(Assert.Single(await new ResourceManagementStore(area.Db).GetRetainedAsync()).BodyJson,ContentJson.Options)!;
        var moved = Assert.Single(await audio.ListDraftsAsync(snapshot.TextUnits[0].Id)); Assert.Equal(new string('0',64),moved.Target.PronunciationHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => audio.SaveAsync(moved,"needs review",true));
        await audio.ReviewDraftAsync(moved,await audio.GetTargetAsync(snapshot.TextUnits[0].Id));
        await audio.SaveAsync(Assert.Single(await audio.ListDraftsAsync(snapshot.TextUnits[0].Id)),"reviewed",true);
    }

    [Fact]
    public async Task CopiedTextDraftProvenanceDoesNotBlockUpdateOrMutateIndependentEdits()
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0];
        var editor = new EditorCommitStore(area.Db); var draft = await editor.StartAsync((await new SqliteContentDocumentStore(area.Db,new()).GetAsync(doc.Id))!);
        var before = await Scalar(area,"SELECT body_json FROM draft WHERE id=$id",("$id",draft.Id.ToString()));
        using var input = Package("all-change"); var plan = await area.Installer.PlanAsync(input);
        Assert.True(plan.Update!.CanApply); Assert.Equal(0,plan.Update.Retained); await area.Installer.InstallAsync(plan);
        Assert.Equal(before,await Scalar(area,"SELECT body_json FROM draft WHERE id=$id",("$id",draft.Id.ToString())));
        var result = await editor.CommitAsync(draft); Assert.Equal(draft.Body.Annotation!.Id,result.Document.Id); Assert.Equal(doc.Title,result.Document.Title);
        Assert.Equal("1.0.0",result.Document.Source.ResourceVersion);
    }

    [Theory]
    [InlineData("all-change")]
    [InlineData("add-remove")]
    public async Task TeachingExampleAndHighlightFollowExactOldGraph(string change)
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area)).Single(d => d.Source.EntryId == "entry-1");
        var item = Teaching(doc); await AddTeaching(area,item);
        var original = item["examples"]!.AsArray().Single(e => e!["status"]!.GetValue<string>() == "available")!;
        using var input = Package(change); var plan = await area.Installer.PlanAsync(input);
        Assert.True(plan.Update!.CanApply); Assert.Equal(1,plan.Update.TeachingExamples); Assert.Equal(1,plan.Update.Retained);
        await area.Installer.InstallAsync(plan);
        var snapshot = JsonSerializer.Deserialize<ContentDocument>(Assert.Single(await new ResourceManagementStore(area.Db).GetRetainedAsync()).BodyJson,ContentJson.Options)!;
        var saved = JsonNode.Parse((string)(await Scalar(area,"SELECT body_json FROM pinyin_item"))!)!;
        var example = saved["examples"]!.AsArray().Single(e => e!["status"]!.GetValue<string>() == "available")!;
        Assert.Equal(snapshot.Id.ToString(),example["contentId"]!.GetValue<string>());
        Assert.Equal(snapshot.Id.ToString(),example["highlight"]!["contentId"]!.GetValue<string>());
        var unit = snapshot.TextUnits.Single(u => u.Id.ToString() == example["highlight"]!["unitId"]!.GetValue<string>());
        var token = unit.Tokens.Single(t => t.Id.ToString() == example["highlight"]!["tokenId"]!.GetValue<string>());
        Assert.Equal(example["tone"]!.GetValue<int>(),token.Pinyin!.Tone);
        Assert.Equal(original["note"]!.GetValue<string>(),example["note"]!.GetValue<string>());
        Assert.Equal(original["highlight"]!["pinyinElementStart"]!.GetValue<int>(),example["highlight"]!["pinyinElementStart"]!.GetValue<int>());
        Assert.Equal("approved",saved["reviewStatus"]!.GetValue<string>());
        Assert.Equal(item["id"]!.GetValue<string>(),saved["id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("wrong-unit")]
    [InlineData("highlight")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-tone")]
    public async Task UnprovableTeachingReferencesStillBlock(string fault)
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0]; var item = Teaching(doc);
        var example=item["examples"]!.AsArray().Single(e => e!["status"]!.GetValue<string>() == "available")!;
        switch(fault) { case "wrong-unit":example["highlight"]!["unitId"]=Guid.NewGuid().ToString();break;
            case "highlight":example["highlight"]!["pinyinElementStart"]=1000;break;
            case "unknown-field":item["futureReference"]=doc.Id.ToString();break;
            default:item["examples"]![0]!["tone"]=item["examples"]![1]!["tone"]!.DeepClone();break; }
        await AddTeaching(area,item);using var input=Package("all-change");var plan=await area.Installer.PlanAsync(input);Assert.False(plan.Update!.CanApply);
        await Assert.ThrowsAsync<PackageException>(()=>area.Installer.InstallAsync(plan));Assert.Equal("1.0.0",await Scalar(area,"SELECT version FROM installed_resource"));
    }

    [Fact]
    public async Task UnfinishedRealRecordingBlocksUntilFinalizedAndLateDraftChangeInvalidatesPlan()
    {
        using var area=new Area();await Seed(area);var doc=(await Documents(area))[0];var audio=new LocalAudioStore(area.Db);
        var draft=await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id),"user");
        using var input=Package("all-change");Assert.False((await area.Installer.PlanAsync(input)).Update!.CanApply);
        await File.WriteAllBytesAsync(audio.CapturePath(draft.Id),Wave());var ready=await audio.FinalizeAsync(draft.Id);
        input.Position=0;var plan=await area.Installer.PlanAsync(input);Assert.True(plan.Update!.CanApply);
        await audio.ReviewDraftAsync(ready,await audio.GetTargetAsync(doc.TextUnits[0].Id));
        Assert.Equal("PLAN_STALE",(await Assert.ThrowsAsync<PackageException>(()=>area.Installer.InstallAsync(plan))).Code);
    }

    [Fact]
    public async Task FailureRollsBackDraftAndTeachingReferencesAlongWithSnapshot()
    {
        using var area=new Area();await Seed(area);var doc=(await Documents(area))[0];var audio=new LocalAudioStore(area.Db);
        var draft=await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id),"user");await File.WriteAllBytesAsync(audio.CapturePath(draft.Id),Wave());await audio.FinalizeAsync(draft.Id);
        await AddTeaching(area,Teaching(doc));var beforeDraft=await Scalar(area,"SELECT body_json FROM draft");var beforeTeaching=await Scalar(area,"SELECT body_json FROM pinyin_item");
        await Execute(area,"CREATE TRIGGER reject_reference_update BEFORE UPDATE OF version ON installed_resource BEGIN SELECT RAISE(ABORT,'test failure'); END");
        using var input=Package("all-change");var plan=await area.Installer.PlanAsync(input);await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(()=>area.Installer.InstallAsync(plan));
        Assert.Equal(beforeDraft,await Scalar(area,"SELECT body_json FROM draft"));Assert.Equal(beforeTeaching,await Scalar(area,"SELECT body_json FROM pinyin_item"));Assert.Equal(0L,await Scalar(area,"SELECT count(*) FROM retained_content"));
        await Execute(area,"DROP TRIGGER reject_reference_update");await area.Installer.InstallAsync(plan);Assert.Equal(1L,await Scalar(area,"SELECT count(*) FROM retained_content"));
    }

    private static JsonNode Teaching(ContentDocument doc)
    {
        var unit=doc.TextUnits.First(u=>u.Tokens.Any(t=>t.Pinyin is {Tone:>=1 and <=4}));var token=unit.Tokens.First(t=>t.Pinyin is {Tone:>=1 and <=4});
        return new JsonObject { ["id"]=Guid.NewGuid().ToString(),["groupCode"]="wholeSyllable",["display"]=token.Pinyin!.Base,["sortOrder"]=0,["demoAudioAssetId"]=null,["reviewStatus"]="approved",
            ["examples"]=new JsonArray(Enumerable.Range(1,4).Select(tone=>(JsonNode)new JsonObject {
                ["tone"]=tone,["status"]=tone==token.Pinyin.Tone?"available":"unavailable",["contentId"]=tone==token.Pinyin.Tone?doc.Id.ToString():null,
                ["highlight"]=tone==token.Pinyin.Tone?new JsonObject{["contentId"]=doc.Id.ToString(),["unitId"]=unit.Id.ToString(),["tokenId"]=token.Id.ToString(),["pinyinElementStart"]=0,["pinyinElementLength"]=new System.Globalization.StringInfo(token.Pinyin.Display).LengthInTextElements}:null,
                ["note"]="Keep literal source UUID "+doc.Id }).ToArray()) };
    }
    private static Task AddTeaching(Area area,JsonNode item)=>Execute(area,"INSERT INTO pinyin_item VALUES($id,$group,$display,0,$body)",("$id",item["id"]!.GetValue<string>()),("$group",item["groupCode"]!.GetValue<string>()),("$display",item["display"]!.GetValue<string>()),("$body",item.ToJsonString()));

    [Fact]
    public async Task TeachingRowReferencingTwoUpdatedEntriesAccumulatesBothMappings()
    {
        using var area=new Area();await Seed(area);var docs=await Documents(area);
        var candidates=docs.SelectMany(d=>d.TextUnits.SelectMany(u=>u.Tokens.Where(t=>t.Pinyin is {Tone:>=1 and <=4}).Select(t=>(Doc:d,Unit:u,Token:t)))).ToArray();
        var pair=(from a in candidates from b in candidates where a.Doc.Id!=b.Doc.Id && a.Token.Pinyin!.Tone!=b.Token.Pinyin!.Tone && a.Token.Pinyin.Base[0]==b.Token.Pinyin.Base[0] select new[]{a,b}).First();
        var item=Teaching(pair[0].Doc);item["display"]=pair[0].Token.Pinyin!.Base[..1];
        foreach(var e in item["examples"]!.AsArray()) {e!["status"]="unavailable";e["contentId"]=null;e["highlight"]=null;}
        foreach(var value in pair)
        {
            var e=item["examples"]!.AsArray().Single(e=>e!["tone"]!.GetValue<int>()==value.Token.Pinyin!.Tone)!;
            e["status"]="available";e["contentId"]=value.Doc.Id.ToString();e["highlight"]=new JsonObject{["contentId"]=value.Doc.Id.ToString(),["unitId"]=value.Unit.Id.ToString(),["tokenId"]=value.Token.Id.ToString(),["pinyinElementStart"]=0,["pinyinElementLength"]=1};
        }
        await AddTeaching(area,item);using var input=Package("all-change");var plan=await area.Installer.PlanAsync(input);Assert.True(plan.Update!.CanApply);Assert.Equal(2,plan.Update.TeachingExamples);
        await area.Installer.InstallAsync(plan);var retained=await new ResourceManagementStore(area.Db).GetRetainedAsync();Assert.Equal(2,retained.Count);
        var saved=JsonNode.Parse((string)(await Scalar(area,"SELECT body_json FROM pinyin_item"))!)!;
        Assert.All(saved["examples"]!.AsArray().Where(e=>e!["status"]!.GetValue<string>()=="available"),e=>Assert.Contains(retained,r=>r.Id.ToString()==e!["contentId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task UnknownAudioDraftFieldCannotBeSilentlyDroppedByMigration()
    {
        using var area=new Area();await Seed(area);var doc=(await Documents(area))[0];var audio=new LocalAudioStore(area.Db);
        var draft=await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id),"user");await File.WriteAllBytesAsync(audio.CapturePath(draft.Id),Wave());await audio.FinalizeAsync(draft.Id);
        await Execute(area,"UPDATE draft SET body_json=json_set(body_json,'$.Draft.FutureReference',$id)",("$id",doc.Id.ToString()));
        using var input=Package("all-change");var plan=await area.Installer.PlanAsync(input);Assert.False(plan.Update!.CanApply);
        await Assert.ThrowsAsync<PackageException>(()=>area.Installer.InstallAsync(plan));Assert.Equal("1.0.0",await Scalar(area,"SELECT version FROM installed_resource"));
    }

    [Fact]
    public async Task ChangedGraphsRetainFavoritesAudioDefaultsAndGrammarWithoutRebindingToNewText()
    {
        using var area = new Area(); await Seed(area); var before = await Documents(area);
        var audio = new LocalAudioStore(area.Db); var voices = new Dictionary<Guid, Guid>();
        await Execute(area, "INSERT INTO favorite_folder(id,name,name_key) VALUES('folder','saved','saved')");
        foreach (var doc in before)
        {
            await Execute(area, "INSERT INTO favorite_item VALUES('folder',$id,7,'2026-09-18T00:00:00Z')", ("$id", doc.Id.ToString()));
            var draft = await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id), "user");
            await File.WriteAllBytesAsync(audio.CapturePath(draft.Id), Wave());
            voices[doc.Id] = await audio.SaveAsync(await audio.FinalizeAsync(draft.Id), "old voice", true);
        }
        var copy = EditorCommitStore.Copy(before[0]); await new SqliteContentDocumentStore(area.Db, new()).SaveAsync(copy, 0);
        using var update = Package("all-change"); var plan = await area.Installer.PlanAsync(update);
        Assert.Equal(4, plan.Update!.Changed); Assert.Equal(4, plan.Update.Retained); Assert.True(plan.Update.CanApply);
        await area.Installer.InstallAsync(plan);
        var retained = await new ResourceManagementStore(area.Db).GetRetainedAsync(); Assert.Equal(4, retained.Count);
        foreach (var old in before)
        {
            var snapshot = retained.Select(r => JsonSerializer.Deserialize<ContentDocument>(r.BodyJson, ContentJson.Options)!).Single(d => d.Source.EntryId == old.Source.EntryId);
            Assert.NotEqual(old.Id, snapshot.Id); Assert.Equal(old.Title, snapshot.Title); Assert.Equal("1.0.0", snapshot.Source.ResourceVersion);
            Assert.True(new ContentDocumentValidator().Validate(snapshot).IsValid);
            Assert.Equal(old.TextUnits.SelectMany(u => u.Tokens).Select(t => (t.Text,t.Pinyin,t.Locked)), snapshot.TextUnits.SelectMany(u => u.Tokens).Select(t => (t.Text,t.Pinyin,t.Locked)));
            var track = Assert.Single(await audio.ListTracksAsync(snapshot.TextUnits[0].Id));
            Assert.Equal(voices[old.Id], track.Id); Assert.True(track.Preferred); Assert.True(track.Eligible);
            Assert.Empty(await audio.ListTracksAsync(old.TextUnits[0].Id));
            await using var playback = await audio.OpenPlaybackAsync("target", snapshot.TextUnits[0].Id);
            using var bytes = new MemoryStream(); await playback.Stream.CopyToAsync(bytes); Assert.Equal(Wave(), bytes.ToArray());
            Assert.Equal(1L, await Scalar(area, "SELECT count(*) FROM favorite_item WHERE content_id=$id AND sort_order=7 AND added_at_utc='2026-09-18T00:00:00Z'", ("$id", snapshot.Id.ToString())));
            var current = (await new SqliteContentDocumentStore(area.Db, new()).GetAsync(old.Id))!;
            Assert.True(current.RowRevision > 1); Assert.True(current.MembershipRevision > 1); Assert.StartsWith("Updated ", current.Document.Title);
        }
        Assert.Equal(copy.Title, (await new SqliteContentDocumentStore(area.Db, new()).GetAsync(copy.Id))!.Document.Title);
        Assert.True((await area.Installer.InstallAsync(plan)).AlreadyInstalled);
        update.Position = 0; Assert.True((await area.Installer.InstallAsync(await area.Installer.PlanAsync(update))).AlreadyInstalled);
        Assert.Equal(1L, await Scalar(area, "SELECT count(*) FROM resource_operation WHERE operation_type='update'"));
        Assert.Equal(0L, await Scalar(area, "SELECT count(*) FROM pragma_foreign_key_check"));
    }

    [Fact]
    public async Task AddedRemovedAndUnchangedEntriesPreserveUserStateAndOldOverrideOnReintroduction()
    {
        using var area = new Area(); await Seed(area); var before = await Documents(area); var removed = before.Single(d => d.Source.EntryId == "entry-1");
        await Execute(area, "UPDATE installed_resource SET enabled=0,priority=17; INSERT INTO resource_entry_override VALUES($resource,$entry,1,'2026-09-18T00:00:00Z')", ("$resource", removed.Source.ResourceId!.Value.ToString()), ("$entry", removed.Source.EntryId!));
        using var update = Package("add-remove"); var plan = await area.Installer.PlanAsync(update);
        Assert.Equal(1, plan.Update!.Added); Assert.Equal(1, plan.Update.Removed); Assert.Equal(3, plan.Update.Unchanged); Assert.Equal(0, plan.Update.Retained);
        await area.Installer.InstallAsync(plan);
        Assert.Null(await new SqliteContentDocumentStore(area.Db, new()).GetAsync(removed.Id));
        var state = Assert.Single(await new ResourceManagementStore(area.Db).GetAsync());
        Assert.False(state.State.IsEnabled); Assert.Equal(17, state.State.Priority); Assert.Equal(1, state.RemovedCount); Assert.Equal(4, state.Count);
        using var next = Package("none", "3.0.0"); await area.Installer.InstallAsync(await area.Installer.PlanAsync(next));
        Assert.NotNull(await new SqliteContentDocumentStore(area.Db, new()).GetAsync(removed.Id));
        Assert.Equal(1L, await Scalar(area, "SELECT removed FROM resource_entry_override WHERE entry_id=$entry", ("$entry", removed.Source.EntryId!)));
    }

    [Fact]
    public async Task UnchangedContentKeepsAudioIdsAndDefaultsInPlace()
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0]; var audio = new LocalAudioStore(area.Db);
        var draft = await audio.BeginAsync(await audio.GetTargetAsync(doc.TextUnits[0].Id), "user"); await File.WriteAllBytesAsync(audio.CapturePath(draft.Id), Wave());
        var binding = await audio.SaveAsync(await audio.FinalizeAsync(draft.Id), "voice", true);
        using var update = Package("none"); var plan = await area.Installer.PlanAsync(update); Assert.Equal(4, plan.Update!.Unchanged);
        await area.Installer.InstallAsync(plan); var track = Assert.Single(await audio.ListTracksAsync(doc.TextUnits[0].Id));
        Assert.Equal(binding, track.Id); Assert.True(track.Eligible); Assert.True(track.Preferred); Assert.Empty(await new ResourceManagementStore(area.Db).GetRetainedAsync());
    }

    [Fact]
    public async Task PronunciationChangeRetainsTheOldConfirmedRecordingOnItsOriginalReading()
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area)).Single(d => d.Kind == ContentKind.Word);
        var audio = new LocalAudioStore(area.Db); var original = await audio.GetTargetAsync(doc.TextUnits[0].Id);
        var draft = await audio.BeginAsync(original,"user"); await File.WriteAllBytesAsync(audio.CapturePath(draft.Id),Wave());
        await audio.SaveAsync(await audio.FinalizeAsync(draft.Id),"original reading",true);
        using var input = Package("none", mutate: docs =>
        {
            var node = docs.Single(d => d!["id"]!.GetValue<string>() == doc.Id.ToString());
            var token = doc.TextUnits[0].Tokens.First(t => t.Pinyin is not null);
            var corrected = DraftAnnotation.Correct(node!.Deserialize<ContentDocument>(ContentJson.Options)!,token.Id,token.Pinyin!.Tone == 4 ? "ni3" : "ni4");
            docs[docs.IndexOf(node)] = JsonSerializer.SerializeToNode(corrected,ContentJson.Options);
        });
        var plan = await area.Installer.PlanAsync(input); Assert.Equal(1,plan.Update!.Changed); await area.Installer.InstallAsync(plan);
        Assert.NotEqual(original.PronunciationHash,(await audio.GetTargetAsync(original.Id)).PronunciationHash);
        var snapshot = JsonSerializer.Deserialize<ContentDocument>(Assert.Single(await new ResourceManagementStore(area.Db).GetRetainedAsync()).BodyJson,ContentJson.Options)!;
        Assert.Equal(original.PronunciationHash,(await audio.GetTargetAsync(snapshot.TextUnits[0].Id)).PronunciationHash);
        Assert.True(Assert.Single(await audio.ListTracksAsync(snapshot.TextUnits[0].Id)).Eligible);
        await Assert.ThrowsAsync<InvalidOperationException>(() => audio.OpenPlaybackAsync("target",original.Id));
    }

    [Fact]
    public async Task RemovedEntryKeepsItsFullGraphAndImportMappings()
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area)).Single(d => d.Source.EntryId == "entry-1");
        await Execute(area,"INSERT INTO import_receipt VALUES('package',printf('%064d',0),printf('%064d',0),'2026-09-18T00:00:00Z','{}'); INSERT INTO import_mapping VALUES('test','content','source','hash',$content,'package'); INSERT INTO import_mapping VALUES('test','unit','unit','hash',$unit,'package')",
            ("$content",doc.Id.ToString()),("$unit",doc.TextUnits[0].Id.ToString().ToUpperInvariant()));
        using var input = Package("add-remove"); var plan = await area.Installer.PlanAsync(input); Assert.Equal(1,plan.Update!.Retained); await area.Installer.InstallAsync(plan);
        Assert.Null(await new SqliteContentDocumentStore(area.Db,new()).GetAsync(doc.Id));
        var snapshot = JsonSerializer.Deserialize<ContentDocument>(Assert.Single(await new ResourceManagementStore(area.Db).GetRetainedAsync()).BodyJson,ContentJson.Options)!;
        Assert.Equal(snapshot.Id.ToString(),await Scalar(area,"SELECT local_id FROM import_mapping WHERE entity_type='content'"));
        Assert.Equal(snapshot.TextUnits[0].Id.ToString(),await Scalar(area,"SELECT local_id FROM import_mapping WHERE entity_type='unit'"));
        Assert.True(new ContentDocumentValidator().Validate(snapshot).IsValid);
    }

    [Fact]
    public async Task ForeignNestedIdentityCollisionIsRejectedDuringPreview()
    {
        using var area = new Area(); await Seed(area); var copy = EditorCommitStore.Copy((await Documents(area))[0]);
        await new SqliteContentDocumentStore(area.Db,new()).SaveAsync(copy,0);
        using var input = Package("none", mutate: docs =>
        {
            var old = docs[0]!["textUnits"]![0]!["id"]!.GetValue<string>(); var replacement = copy.TextUnits[0].Id.ToString();
            // Only replace schema identity/reference occurrences in this engineering fixture.
            docs[0] = JsonNode.Parse(docs[0]!.ToJsonString().Replace(old,replacement,StringComparison.Ordinal));
        });
        Assert.Equal("RESOURCE_CONTENT_CONFLICT",(await Assert.ThrowsAsync<PackageException>(() => area.Installer.PlanAsync(input))).Code);
        Assert.Equal(copy.Title,(await new SqliteContentDocumentStore(area.Db,new()).GetAsync(copy.Id))!.Document.Title);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("teaching")]
    public async Task UnsupportedReferencesBlockWithoutChangingOldData(string kind)
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0];
        await Execute(area, kind == "draft" ? "INSERT INTO draft VALUES('pending',$content,'audio',1,json_object('TargetId',$unit),'2026-09-18T00:00:00Z')" : "INSERT INTO pinyin_item VALUES('teaching','test','test',0,json_object('exampleUnitId',$unit))",
            ("$content", doc.Id.ToString()), ("$unit", doc.TextUnits[0].Id.ToString().ToUpperInvariant()));
        using var update = Package("all-change"); var plan = await area.Installer.PlanAsync(update); Assert.False(plan.Update!.CanApply);
        Assert.Equal("RESOURCE_UPDATE_REFERENCES_BLOCKED", (await Assert.ThrowsAsync<PackageException>(() => area.Installer.InstallAsync(plan))).Code);
        Assert.Equal("1.0.0", await Scalar(area, "SELECT version FROM installed_resource")); Assert.Equal(4L, await Scalar(area, "SELECT count(*) FROM content"));
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("draft")]
    [InlineData("epoch")]
    public async Task PreviewCannotHideReferencesAddedBeforeCommit(string change)
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0];
        using var update = Package("all-change"); var plan = await area.Installer.PlanAsync(update);
        await Execute(area, change switch {
            "favorite" => "INSERT INTO favorite_folder(id,name,name_key) VALUES('f','f','f'); INSERT INTO favorite_item VALUES('f',$id,0,'2026-09-18T00:00:00Z')",
            "draft" => "INSERT INTO draft VALUES('d',$id,'content',1,'{}','2026-09-18T00:00:00Z')",
            _ => "UPDATE app_state SET data_epoch=data_epoch+1" }, ("$id", doc.Id.ToString()));
        Assert.Equal("PLAN_STALE", (await Assert.ThrowsAsync<PackageException>(() => area.Installer.InstallAsync(plan))).Code);
        Assert.Equal("1.0.0", await Scalar(area, "SELECT version FROM installed_resource"));
    }

    [Fact]
    public async Task FailureAfterSnapshotMigrationRollsBackEntireUpdateAndCanRetry()
    {
        using var area = new Area(); await Seed(area); var doc = (await Documents(area))[0];
        await Execute(area, "INSERT INTO favorite_folder(id,name,name_key) VALUES('f','f','f'); INSERT INTO favorite_item VALUES('f',$id,0,'2026-09-18T00:00:00Z'); CREATE TRIGGER reject_update BEFORE UPDATE OF version ON installed_resource BEGIN SELECT RAISE(ABORT,'test failure'); END", ("$id", doc.Id.ToString()));
        using var update = Package("all-change"); var plan = await area.Installer.PlanAsync(update);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => area.Installer.InstallAsync(plan));
        Assert.Equal(doc.Id.ToString(), await Scalar(area, "SELECT content_id FROM favorite_item"));
        Assert.Equal(0L, await Scalar(area, "SELECT count(*) FROM retained_content")); Assert.Equal("1.0.0", await Scalar(area, "SELECT version FROM installed_resource"));
        Assert.Equal(0L, await Scalar(area, "SELECT count(*) FROM resource_operation WHERE operation_type='update'"));
        await Execute(area, "DROP TRIGGER reject_update"); await area.Installer.InstallAsync(plan); Assert.Equal("2.0.0", await Scalar(area, "SELECT version FROM installed_resource"));
    }

    [Fact]
    public async Task VersionOrderIsNumericAndRejectsDowngradeOrKindChange()
    {
        using var area = new Area(); await Seed(area);
        using var higher = Package("none", "10.0.0"); await area.Installer.InstallAsync(await area.Installer.PlanAsync(higher));
        using var lower = Package("none", "2.0.0"); Assert.Equal("RESOURCE_DOWNGRADE_REJECTED", (await Assert.ThrowsAsync<PackageException>(() => area.Installer.PlanAsync(lower))).Code);
        using var kind = Package("dictionary", "11.0.0"); Assert.Equal("RESOURCE_KIND_MISMATCH", (await Assert.ThrowsAsync<PackageException>(() => area.Installer.PlanAsync(kind))).Code);
        using var publisher = Package("publisher", "11.0.0"); Assert.True((await area.Installer.PlanAsync(publisher)).Update!.PublisherChanged);
    }

    [Fact]
    public async Task UninstalledResourceCannotBeSilentlyReactivatedByUpdateAndCancellationDoesNotWrite()
    {
        using var area = new Area(); await Seed(area); using var input = Package("none"); var plan = await area.Installer.PlanAsync(input);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => area.Installer.InstallAsync(plan, new CancellationToken(true)));
        var management = new ResourceManagementStore(area.Db); await management.UninstallRetainingAsync(await management.PreviewRetainingUninstallAsync(Assert.Single(await management.GetAsync())));
        input.Position = 0; var reinstall = await area.Installer.PlanAsync(input);
        Assert.NotNull(reinstall.Update);
        Assert.False(Assert.Single(await management.GetAsync()).State.IsPresent);
        await area.Installer.InstallAsync(reinstall);
        var restored = Assert.Single(await management.GetAsync());
        Assert.True(restored.State.IsPresent); Assert.False(restored.State.IsEnabled);
        Assert.Equal("2.0.0", restored.State.Version);
    }

    private static async Task Seed(Area area)
    { using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","sample-learning.hanresource")); await area.Installer.InstallAsync(await area.Installer.PlanAsync(input)); }
    private static async Task<ContentDocument[]> Documents(Area area) => (await area.Installer.GetLibraryAsync()).Select(r => JsonSerializer.Deserialize<ContentDocument>(r.BodyJson, ContentJson.Options)!).ToArray();
    private static byte[] Wave() { using var s = new MemoryStream(); s.SetLength(16044); PcmWave.WriteHeader(s,8000,1,16000); return s.ToArray(); }
    private static MemoryStream Package(string change, string version = "2.0.0", Action<JsonArray>? mutate = null)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","sample-learning.hanresource")); using var zip = new ZipArchive(input);
        var files = zip.Entries.ToDictionary(e => e.FullName, e => { using var s=e.Open(); using var m=new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
        var manifest = JsonNode.Parse(files["manifest.json"])!; var descriptor = JsonNode.Parse(files["resource.json"])!; var contents = JsonNode.Parse(files["contents.json"])!;
        var docs = contents["contents"]!.AsArray(); descriptor["version"] = version;
        foreach (var doc in docs) { doc!["source"]!["resourceVersion"] = version; if (change == "all-change") doc["title"] = "Updated " + doc["title"]!.GetValue<string>(); }
        if (change == "publisher") descriptor["publisherName"] = "Different publisher";
        if (change == "dictionary") { descriptor["resourceKind"] = "dictionary"; foreach(var doc in docs.Where(d => d!["kind"]!.GetValue<string>() != "word").ToArray()) docs.Remove(doc); }
        if (change == "add-remove")
        {
            docs.Remove(docs.Single(d => d!["source"]!["entryId"]!.GetValue<string>() == "entry-1"));
            var copy = EditorCommitStore.Copy(docs.First()!.Deserialize<ContentDocument>(ContentJson.Options)!);
            var resource = Guid.Parse(descriptor["resourceId"]!.GetValue<string>()); var hash = SHA1.HashData(resource.ToByteArray(bigEndian:true).Concat(Encoding.UTF8.GetBytes("new-entry")).ToArray());
            hash[6]=(byte)((hash[6]&15)|0x50); hash[8]=(byte)((hash[8]&63)|0x80);
            copy = copy with { Id=new Guid(hash.AsSpan(0,16),bigEndian:true), Origin=ContentOrigin.Resource, Source=copy.Source with { EntryId="new-entry" } };
            docs.Add(JsonSerializer.SerializeToNode(copy,ContentJson.Options));
        }
        mutate?.Invoke(docs);
        descriptor["entries"] = new JsonArray(docs.Select(d => (JsonNode)new JsonObject { ["entryId"]=d!["source"]!["entryId"]!.DeepClone(), ["contentId"]=d["id"]!.DeepClone() }).ToArray());
        manifest["counts"]!["contents"] = docs.Count;
        files["contents.json"] = Encoding.UTF8.GetBytes(contents.ToJsonString()); files["resource.json"] = Encoding.UTF8.GetBytes(descriptor.ToJsonString());
        foreach (var entry in manifest["files"]!.AsArray()) { var bytes=files[entry!["path"]!.GetValue<string>()]; entry["byteLength"]=bytes.Length; entry["sha256"]=Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        files["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString()); var result=new MemoryStream();
        using(var output=new ZipArchive(result,ZipArchiveMode.Create,true)) foreach(var (path,bytes) in files) { using var stream=output.CreateEntry(path).Open();stream.Write(bytes); }
        result.Position=0;return result;
    }
    private static async Task<object?> Scalar(Area area,string sql,params (string,object)[] args)
    { using var c=await area.Db.OpenConnectionAsync();using var cmd=c.CreateCommand();cmd.CommandText=sql;foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);return await cmd.ExecuteScalarAsync(); }
    private static async Task Execute(Area area,string sql,params (string,object)[] args)
    { using var c=await area.Db.OpenConnectionAsync();using var cmd=c.CreateCommand();cmd.CommandText=sql;foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);await cmd.ExecuteNonQueryAsync(); }
    private sealed class Area : IDisposable
    {
        private readonly string _root=Path.Combine(Path.GetTempPath(),"HanMate-resource-update-tests",Guid.NewGuid().ToString("N"));
        public HanMateDatabase Db { get; }
        public TextResourceInstaller Installer { get; }
        public Area() { Directory.CreateDirectory(_root); Db=new(Path.Combine(_root,"hanmate.db"));Installer=new(Db); }
        public void Dispose() { Directory.Delete(_root,true); }
    }
}
