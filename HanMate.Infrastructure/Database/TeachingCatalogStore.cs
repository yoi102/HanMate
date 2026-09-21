using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Core.Pinyin;
using HanMate.Infrastructure.Packages;
using Microsoft.Data.Sqlite;
using static HanMate.Infrastructure.Packages.BackupSnapshot;

namespace HanMate.Infrastructure.Database;

public sealed class TeachingCatalogPlan
{
    internal TeachingCatalogPlan(JsonNode catalog,string guard,string path) { Catalog=catalog; Guard=guard; Path=path; }
    internal JsonNode Catalog { get; }
    internal string Guard { get; }
    internal string Path { get; }
    public int Count => Catalog["items"]!.AsArray().Count;
}
/// <summary>Local teaching catalog references existing validated content; it never manufactures pronunciation audio.</summary>
public sealed class TeachingCatalogStore(HanMateDatabase database)
{
    public async Task<TeachingCatalogPlan> PlanAsync(Stream input,CancellationToken token=default)
    {
        using var bytes=new MemoryStream(); var buffer=new byte[8192]; int count;
        while ((count=await input.ReadAsync(buffer,token))>0) { if(bytes.Length+count>1024*1024) throw new PackageException("PACKAGE_LIMIT_EXCEEDED"); bytes.Write(buffer,0,count); }
        var catalog=PackageJson.Parse(bytes.ToArray()); PackageJson.Validate("pinyin-catalog.schema.json",catalog);
        if(catalog["items"]!.AsArray().Count is <1 or >200) throw new PackageException("PACKAGE_LIMIT_EXCEEDED");
        await database.InitializeAsync(token); using var c=await database.OpenConnectionAsync(token); using var tx=c.BeginTransaction(deferred:true);
        await Validate(c,tx,catalog["items"]!.AsArray(),token);
        await CheckCapacity(c,tx,catalog["items"]!.AsArray(),token);
        foreach(var item in catalog["items"]!.AsArray())
        {
            var id=item!["id"]!.GetValue<string>();
            var existing=await Scalar(c,tx,"SELECT body_json FROM pinyin_item WHERE id=$id",token,("$id",id)) as string;
            if(existing is not null && !JsonNode.DeepEquals(JsonNode.Parse(existing),item)) throw new PackageException("RESOURCE_CONTENT_CONFLICT");
            if(Convert.ToInt32(await Scalar(c,tx,"SELECT count(*) FROM (SELECT j.value AS id FROM content c,json_tree(c.body_json) j WHERE j.key='id' UNION SELECT id FROM audio_asset UNION SELECT id FROM audio_binding UNION SELECT resource_id FROM installed_resource UNION SELECT id FROM favorite_folder) WHERE id=$id",token,("$id",id)))>0) throw new PackageException("PACKAGE_DUPLICATE_ID");
        }
        return new(catalog,await GuardAsync(c,tx,token),database.DatabasePath);
    }
    public async Task CommitAsync(TeachingCatalogPlan plan,CancellationToken token=default)
    {
        if(plan.Path!=database.DatabasePath) throw new PackageException("PLAN_STALE");
        using var c=await database.OpenConnectionAsync(token); using var tx=c.BeginTransaction();
        if(await GuardAsync(c,tx,token)!=plan.Guard) throw new PackageException("PLAN_STALE");
        await CheckCapacity(c,tx,plan.Catalog["items"]!.AsArray(),token);
        foreach(var n in plan.Catalog["items"]!.AsArray()) await Execute(c,tx,"INSERT INTO pinyin_item VALUES($id,$group,$display,$sort,$body) ON CONFLICT(id) DO NOTHING",token,
            ("$id",n!["id"]!.GetValue<string>()),("$group",n["groupCode"]!.GetValue<string>()),("$display",n["display"]!.GetValue<string>()),("$sort",n["sortOrder"]!.GetValue<long>()),("$body",n.ToJsonString()));
        await Execute(c,tx,"UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1",token); token.ThrowIfCancellationRequested(); tx.Commit();
    }
    private static async Task CheckCapacity(SqliteConnection c, SqliteTransaction tx, JsonArray items, CancellationToken token)
    {
        var ids = JsonSerializer.Serialize(items.Select(n => n!["id"]!.GetValue<string>()).ToArray());
        var total = Convert.ToInt32(await Scalar(c,tx,
            "SELECT count(*) FROM (SELECT id FROM pinyin_item UNION SELECT value FROM json_each($ids))",token,("$ids",ids)));
        // Match the backup contract across successive imports; reimporting existing IDs consumes no capacity.
        if (total > 200) throw new PackageException("PACKAGE_LIMIT_EXCEEDED");
    }
    public async Task<PinyinCourse> LoadAsync(PinyinCourse bundled,CancellationToken token=default)
    {
        await database.InitializeAsync(token); using var c=await database.OpenConnectionAsync(token); using var tx=c.BeginTransaction(deferred:true);
        var items=new JsonArray();
        using(var cmd=Command(c,tx,"SELECT body_json FROM pinyin_item ORDER BY sort_order,id"))
        using(var r=await cmd.ExecuteReaderAsync(token)) while(await r.ReadAsync(token)) items.Add(PackageJson.Parse(System.Text.Encoding.UTF8.GetBytes(r.GetString(0))));
        if(items.Count==0) return bundled;
        var external=await Validate(c,tx,items,token,visibleOnly:true);
        var docs=bundled.Data.Contents.Concat(external.Data.Contents).GroupBy(d=>d.Id).Select(g=>g.Last()).ToArray();
        var rows=bundled.Data.Items.Concat(external.Data.Items).GroupBy(i=>i.Id).Select(g=>g.Last()).ToArray();
        var keys=external.Data.Items.SelectMany(i=>i.Examples).Where(e=>external.PlaybackKey(e) is not null).GroupBy(e=>e.UnitId).ToDictionary(g=>g.Key,g=>external.PlaybackKey(g.First())!);
        return new(bundled.Data with { Items=rows,Contents=docs },keys);
    }
    private static async Task<PinyinCourse> Validate(SqliteConnection c,SqliteTransaction tx,JsonArray nodes,CancellationToken token,bool visibleOnly=false)
    {
        PackageJson.Validate("pinyin-catalog.schema.json",new JsonObject { ["schemaVersion"]=1,["catalogId"]=Guid.NewGuid().ToString(),["catalogVersion"]="local",["items"]=nodes.DeepClone() });
        var docs=new Dictionary<Guid,ContentDocument>(); var items=new List<PinyinTeachingItem>(); var keys=new Dictionary<Guid,string>(); var ids=new HashSet<Guid>();
        foreach(var n in nodes)
        {
            var id=Guid.Parse(n!["id"]!.GetValue<string>()); if(id==Guid.Empty||!ids.Add(id)) throw new PackageException("PACKAGE_DUPLICATE_ID");
            var examples=new List<PinyinExample>(); var hidden=false; var tones=new HashSet<int>();
            foreach(var e in n["examples"]!.AsArray())
            {
                var tone=e!["tone"]!.GetValue<int>(); if(!tones.Add(tone)) throw new PackageException("PACKAGE_CONTENT_INVALID");
                if(e["status"]!.GetValue<string>()=="unavailable") { if(e["contentId"] is not null||e["highlight"] is not null) throw new PackageException("PACKAGE_CONTENT_INVALID"); continue; }
                var content=Guid.Parse(e["contentId"]!.GetValue<string>()); var h=e["highlight"]!;
                if(content!=Guid.Parse(h["contentId"]!.GetValue<string>())) throw new PackageException("PACKAGE_CONTENT_INVALID");
                var json=await Scalar(c,tx,"SELECT c.body_json FROM content c WHERE c.id=$id AND " + ContentTrashStore.Active,token,("$id",content.ToString())) as string ?? throw new PackageException("PACKAGE_CONTENT_INVALID");
                var d=JsonSerializer.Deserialize<ContentDocument>(json,ContentJson.Options)!; docs[content]=d;
                if(d.Origin==ContentOrigin.Resource && Convert.ToInt32(await Scalar(c,tx,"SELECT count(*) FROM resource_entry e JOIN installed_resource r ON r.resource_id=e.resource_id LEFT JOIN resource_entry_override o ON o.resource_id=e.resource_id AND o.entry_id=e.entry_id WHERE e.content_id=$id AND r.is_present=1 AND r.enabled=1 AND COALESCE(o.removed,0)=0",token,("$id",content.ToString())))==0) hidden=true;
                var unit=d.TextUnits.Single(u=>u.Id==Guid.Parse(h["unitId"]!.GetValue<string>())); var t=unit.Tokens.Single(t=>t.Id==Guid.Parse(h["tokenId"]!.GetValue<string>()));
                if(t.Pinyin is null) throw new PackageException("PACKAGE_CONTENT_INVALID");
                examples.Add(new(tone,content,unit.Id,t.Id,t.Pinyin.Base+tone,h["pinyinElementStart"]!.GetValue<int>(),h["pinyinElementLength"]!.GetValue<int>()));
                var hashes=PackageAudio.Targets([d])[unit.Id];
                if(Convert.ToInt32(await Scalar(c,tx,"SELECT count(*) FROM audio_binding b WHERE b.target_id=$target AND b.review_state='confirmed' AND b.bound_text_hash=$text AND b.bound_pronunciation_hash=$pronunciation",token,("$target",unit.Id.ToString()),("$text",hashes.Text),("$pronunciation",hashes.Pronunciation)))>0)
                    keys[unit.Id]=$"local:checked:{unit.Id}:{content}:{hashes.Text}:{hashes.Pronunciation}";
            }
            if(n["demoAudioAssetId"] is { } demo && Convert.ToInt32(await Scalar(c,tx,"SELECT count(*) FROM audio_binding WHERE asset_id=$id AND source_role='standard' AND target_id IN (SELECT value FROM json_each($targets))",token,("$id",demo.GetValue<string>()),("$targets",JsonSerializer.Serialize(examples.Select(e=>e.UnitId.ToString())))))==0) throw new PackageException("PACKAGE_AUDIO_INVALID");
            var group=n["groupCode"]!.GetValue<string>() switch { "spellingAid"=>"spelling","simpleFinal"=>"simple","compoundFinal"=>"compound","nasalFinal"=>"nasal","wholeSyllable"=>"whole",var value=>value };
            var item=new PinyinTeachingItem(id,group,n["display"]!.GetValue<string>(),examples,group=="spelling");
            _=new PinyinCourse(new("local",[item],docs.Values.ToArray(),[]),keys);
            if(!visibleOnly||!hidden) items.Add(item);
        }
        return new(new("local",items,docs.Values.ToArray(),[]),keys);
    }
}
