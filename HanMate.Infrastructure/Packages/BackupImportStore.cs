using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using static HanMate.Infrastructure.Packages.BackupSnapshot;
using static HanMate.Infrastructure.Packages.BackupPackageCodec;

namespace HanMate.Infrastructure.Packages;

internal sealed record BackupResourceAction(BackupResource Resource, string Mode);
internal sealed record BackupFolderAction(BackupFolder Source, Guid LocalId, string Name, bool Reuse);
public sealed class BackupImportPlan
{
    internal BackupImportPlan(BackupPackage package, ContentImportPlan content, string guard, IReadOnlyList<BackupResourceAction> resources,
        IReadOnlyList<BackupFolderAction> folders, bool applySettings)
    { Package = package; Content = content; Guard = guard; Resources = resources; Folders = folders; ApplySettings = applySettings; }
    internal BackupPackage Package { get; }
    internal ContentImportPlan Content { get; }
    internal string Guard { get; }
    internal IReadOnlyList<BackupResourceAction> Resources { get; }
    internal IReadOnlyList<BackupFolderAction> Folders { get; }
    public bool ApplySettings { get; }
    public int Added => Content.Added;
    public int Reused => Content.Reused;
    public int Conflicts => Content.Conflicts;
    public int AudioAdded => Content.AudioAdded;
    public int Favorites => Package.Collections.Items.Count + (Package.Collections.DictionaryBookmarks?.Count ?? 0);
    public int FoldersAdded => Folders.Count(f => !f.Reuse);
    public int FolderConflicts => Folders.Count(f => f.Reuse || f.Name != f.Source.Name);
    public int ResourcesInstalled => Resources.Count(r => r.Mode == "install");
    public int ResourcesKept => Resources.Count(r => r.Mode == "keep");
    public IReadOnlyList<string> MissingResources => Resources.Where(r => r.Mode is "missing" or "snapshot")
        .Select(r => r.Resource.Descriptor["names"]!["zh-Hans"]!.GetValue<string>() + " · " + r.Resource.Version).ToArray();
}

/// <summary>Non-destructive logical backup merge. Complete replacement is a separate recovery workflow.</summary>
public sealed class BackupImportStore(HanMateDatabase database)
{
    public async Task<BackupImportPlan> PlanAsync(Stream input, bool applySettings = false, CancellationToken token = default)
    {
        var package = await ReadAsync(input, token);
        await new FavoriteStore(database).GetFoldersAsync(token);
        using var c = await database.OpenConnectionAsync(token); using var tx = c.BeginTransaction(deferred: true);
        return await PlanInTransactionAsync(package, applySettings, c, tx, token);
    }
    internal async Task<BackupImportPlan> PlanInTransactionAsync(BackupPackage package, bool applySettings, SqliteConnection c, SqliteTransaction tx, CancellationToken token)
    {
        var guard = await GuardAsync(c, tx, token);
        var occupied = new HashSet<Guid>();
        using (var cmd = Command(c, tx, "SELECT j.value FROM content c,json_tree(c.body_json) j WHERE j.key='id' AND j.type='text' UNION SELECT id FROM audio_asset UNION SELECT id FROM audio_binding UNION SELECT id FROM pinyin_item UNION SELECT id FROM favorite_folder UNION SELECT resource_id FROM installed_resource"))
        using (var r = await cmd.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) if (Guid.TryParse(r.GetString(0), out var id)) occupied.Add(id);
        var actions = new List<BackupResourceAction>();
        foreach (var resource in package.Resources.Resources)
        {
            string? version = null, fingerprint = null; bool present = false;
            using (var cmd = Command(c, tx, "SELECT version,payload_fingerprint,is_present FROM installed_resource WHERE resource_id=$id", ("$id", resource.Id.ToString())))
            using (var r = await cmd.ExecuteReaderAsync(token)) if (await r.ReadAsync(token)) { version = r.GetString(0); fingerprint = r.GetString(1); present = r.GetBoolean(2); }
            var exact = version == resource.Version && fingerprint == resource.PayloadFingerprint;
            var mode = exact && present ? "keep" : "snapshot";
            if (mode == "keep" && resource.PayloadStatus == "included")
            {
                foreach (var d in package.Content.Documents.Where(d => resource.ContentIds.Contains(d.Id)))
                {
                    var json = await Scalar(c, tx, "SELECT c.body_json FROM content c JOIN resource_entry e ON e.content_id=c.id WHERE c.id=$id AND e.resource_id=$resource AND " + ContentTrashStore.Active,
                        token, ("$id", d.Id.ToString()), ("$resource", resource.Id.ToString())) as string;
                    if (json is null || TextContentPackageCodec.Fingerprint(JsonSerializer.Deserialize<ContentDocument>(json, ContentJson.Options)!) != TextContentPackageCodec.Fingerprint(d)) { mode = "snapshot"; break; }
                }
            }
            if (!present && (version is null || exact) && !BundledResourceCatalog.IsReserved(resource.Id))
            {
                if (resource.PayloadStatus == "referenceOnly") mode = "missing";
                else
                {
                    var graphIds = package.Content.Documents.Where(d => resource.ContentIds.Contains(d.Id)).SelectMany(TextContentPackageCodec.Identities).Select(x => x.Id);
                    var published = resource.Descriptor["audioBindingIds"]!.AsArray().Select(n => Guid.Parse(n!.GetValue<string>())).ToHashSet();
                    var audioIds = package.Content.Audio.Bindings.Where(b => published.Contains(b.Id)).SelectMany(b => new[] { b.Id, b.AssetId });
                    if (!graphIds.Concat(audioIds).Any(occupied.Contains)) mode = "install";
                }
            }
            actions.Add(new(resource, mode));
        }
        var registered = Convert.ToInt32(await Scalar(c, tx, "SELECT count(*) FROM installed_resource", token));
        foreach (var action in actions.Where(a => a.Mode is "install" or "missing"))
            if (Convert.ToInt32(await Scalar(c, tx, "SELECT count(*) FROM installed_resource WHERE resource_id=$id", token, ("$id", action.Resource.Id.ToString()))) == 0) registered++;
        Require(registered <= 100, "PACKAGE_LIMIT_EXCEEDED");
        var docs = package.Content.Documents.Select(d => d.Origin switch {
            ContentOrigin.Resource when actions.Single(a => a.Resource.Id == d.Source.ResourceId).Mode is not ("install" or "keep") => d with { Origin = ContentOrigin.Retained },
            ContentOrigin.Builtin => d with { Origin = ContentOrigin.BuiltinSnapshot }, _ => d }).ToArray();
        var content = await new ContentPackageImportStore(database).PlanInTransactionAsync(package.Content with { Documents = docs }, c, tx, token);
        // Installation requires published identities to remain intact, including the audio defaults in the descriptor.
        foreach (var action in actions.Where(a => a.Mode == "install"))
        {
            Require(content.Items.Where(i => action.Resource.ContentIds.Contains(i.Source.Id)).All(i => i.Map.All(p => p.Key == p.Value)), "RESOURCE_IDENTITY_INVALID");
            var published = action.Resource.Descriptor["audioBindingIds"]!.AsArray().Select(n => Guid.Parse(n!.GetValue<string>())).ToHashSet();
            Require(content.Audio.Bindings.Where(b => published.Contains(b.Source.Id)).All(b => b.LocalId == b.Source.Id && b.AssetId == b.Source.AssetId), "RESOURCE_IDENTITY_INVALID");
        }
        var existing = new List<BackupFolder>();
        using (var cmd = Command(c, tx, "SELECT id,name,description,sort_order,system_role FROM favorite_folder ORDER BY sort_order,id"))
        using (var r = await cmd.ExecuteReaderAsync(token)) while (await r.ReadAsync(token)) existing.Add(new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetInt64(3), r.IsDBNull(4) ? null : r.GetString(4)));
        var names = existing.Where(f => f.SystemRole is null).Select(f => NameKey(f.Name)).ToHashSet();
        occupied.UnionWith(content.Items.SelectMany(i => i.Map.Values)); occupied.UnionWith(content.Audio.Assets.Select(a => a.LocalId)); occupied.UnionWith(content.Audio.Bindings.Select(b => b.LocalId));
        var folders = new List<BackupFolderAction>();
        foreach (var f in package.Collections.Folders.OrderBy(f => f.SortOrder))
        {
            var local = f.SystemRole == "default" ? existing.Single(e => e.SystemRole == "default") : existing.FirstOrDefault(e => e.Id == f.Id && e.SystemRole is null);
            if (local is null && f.SystemRole is null)
            {
                var mapped = await Scalar(c, tx, "SELECT local_id FROM import_mapping WHERE source_namespace='backup.folder.v1' AND entity_type='folder' AND source_id=$id AND source_fingerprint=$hash", token,
                    ("$id", f.Id.ToString()), ("$hash", FolderHash(f))) as string;
                if (mapped is not null) local = existing.FirstOrDefault(e => e.Id.ToString() == mapped && e.SystemRole is null);
            }
            if (local is not null) { folders.Add(new(f, local.Id, local.Name, true)); continue; }
            var name = f.Name; var suffix = 1;
            while (!names.Add(NameKey(name)))
            {
                var tail = suffix == 1 ? " (导入)" : $" (导入 {suffix})"; suffix++;
                var info = new StringInfo(f.Name); name = info.SubstringByTextElements(0, Math.Min(info.LengthInTextElements, 40 - new StringInfo(tail).LengthInTextElements)) + tail;
            }
            var id = f.Id; while (!occupied.Add(id)) id = Guid.NewGuid(); folders.Add(new(f, id, name, false));
        }
        return new(package, content, guard, actions, folders, applySettings);
    }
    public Task<ContentImportResult> CommitAsync(BackupImportPlan plan, CancellationToken token = default)
        => new ContentPackageImportStore(database).CommitWithMetadataAsync(plan.Content,
            async (c, tx) => Require(await GuardAsync(c, tx, token) == plan.Guard, "PLAN_STALE"),
            (c, tx) => MergeMetadataAsync(plan, c, tx, token), token);

    internal static async Task MergeMetadataAsync(BackupImportPlan plan, SqliteConnection c, SqliteTransaction tx, CancellationToken token)
    {
        var map = plan.Content.Items.ToDictionary(i => i.Source.Id, i => i.Local.Id);
        foreach (var action in plan.Resources)
        {
            var r = action.Resource;
            if (action.Mode is "install" or "missing")
            {
                await Execute(c, tx, """
                    INSERT INTO installed_resource(resource_id,resource_kind,version,descriptor_json,descriptor_sha256,payload_fingerprint,distribution,is_present,enabled,priority)
                    VALUES($id,$kind,$version,$body,$hash,$fingerprint,'external',$present,$enabled,$priority)
                    ON CONFLICT(resource_id) DO UPDATE SET is_present=$present,enabled=$enabled,priority=$priority,row_revision=row_revision+1
                    """, token, ("$id", r.Id.ToString()), ("$kind", r.Descriptor["resourceKind"]!.GetValue<string>()), ("$version", r.Version),
                    ("$body", r.Descriptor.ToJsonString()), ("$hash", r.DescriptorSha256), ("$fingerprint", r.PayloadFingerprint),
                    ("$present", action.Mode == "install" ? 1 : 0), ("$enabled", action.Mode == "install" && r.Enabled ? 1 : 0), ("$priority", r.Priority));
                if (action.Mode == "install")
                    foreach (var e in r.Descriptor["entries"]!.AsArray()) await Execute(c, tx, "INSERT INTO resource_entry VALUES($resource,$entry,$content)", token,
                        ("$resource", r.Id.ToString()), ("$entry", e!["entryId"]!.GetValue<string>()), ("$content", e["contentId"]!.GetValue<string>()));
                foreach (var entry in r.RemovedEntryIds) await Execute(c, tx, "INSERT INTO resource_entry_override VALUES($resource,$entry,1,$time) ON CONFLICT(resource_id,entry_id) DO UPDATE SET removed=1,updated_at_utc=$time", token,
                    ("$resource", r.Id.ToString()), ("$entry", entry), ("$time", DateTime.UtcNow.ToString("O")));
            }
        }
        foreach (var item in plan.Content.Items.Where(i => i.Local.Origin == ContentOrigin.Retained && !i.Reuse))
        {
            var d = item.Local;
            await Execute(c, tx, "INSERT INTO retained_content VALUES($id,$resource,$entry,$version,'explicitKeep',$time)", token,
                ("$id", d.Id.ToString()), ("$resource", d.Source.ResourceId!.Value.ToString()), ("$entry", d.Source.EntryId!), ("$version", d.Source.ResourceVersion!), ("$time", DateTime.UtcNow.ToString("O")));
        }
        foreach (var f in plan.Folders)
        {
            if (!f.Reuse) await Execute(c, tx, "INSERT INTO favorite_folder(id,name,name_key,description,sort_order) VALUES($id,$name,$key,$description,(SELECT COALESCE(max(sort_order),0)+1 FROM favorite_folder))", token,
                ("$id", f.LocalId.ToString()), ("$name", f.Name), ("$key", NameKey(f.Name)), ("$description", f.Source.Description));
            await Execute(c, tx, "INSERT INTO import_mapping VALUES('backup.folder.v1','folder',$source,$hash,$local,$package) ON CONFLICT(source_namespace,entity_type,source_id,source_fingerprint) DO UPDATE SET local_id=$local", token,
                ("$source", f.Source.Id.ToString()), ("$hash", FolderHash(f.Source)), ("$local", f.LocalId.ToString()), ("$package", plan.Package.Content.Id.ToString()));
        }
        var folderMap = plan.Folders.ToDictionary(f => f.Source.Id, f => f.LocalId);
        foreach (var i in plan.Package.Collections.Items.OrderBy(i => i.SortOrder))
        {
            using var cmd = Command(c, tx, "INSERT INTO favorite_item(folder_id,content_id,sort_order,added_at_utc) VALUES($folder,$content,(SELECT COALESCE(max(sort_order),0)+1 FROM favorite_item WHERE folder_id=$folder),$time) ON CONFLICT(folder_id,content_id) DO NOTHING",
                ("$folder", folderMap[i.FolderId].ToString()), ("$content", map[i.ContentId].ToString()), ("$time", i.AddedAtUtc));
            if (await cmd.ExecuteNonQueryAsync(token) == 1) await Execute(c, tx, "UPDATE content SET membership_revision=membership_revision+1 WHERE id=$id", token, ("$id", map[i.ContentId].ToString()));
        }
        await BackupTeaching.MergeAsync(plan, c, tx, token);
        if (plan.Package.Settings[CustomWordCategoryStore.Key] is JsonArray importedCategories)
        {
            var raw = await Scalar(c, tx, "SELECT body_json FROM user_settings WHERE singleton=1", token) as string;
            var local = JsonNode.Parse(raw ?? "{}")!.AsObject();
            var merged = CustomWordCategoryStore.Read(raw).Concat(CustomWordCategoryStore.Read(new JsonObject
                { [CustomWordCategoryStore.Key] = importedCategories.DeepClone() }.ToJsonString())).DistinctBy(x => x.Id).ToArray();
            local[CustomWordCategoryStore.Key] = new JsonArray(merged.Select(x => (JsonNode?)new JsonObject
                { ["id"] = x.Id, ["name"] = x.Name }).ToArray());
            await Execute(c, tx, "INSERT INTO user_settings VALUES(1,1,1,$body) ON CONFLICT(singleton) DO UPDATE SET row_revision=row_revision+1,body_json=$body", token, ("$body", local.ToJsonString()));
        }
        if (plan.Package.Settings[CustomWordCategoryStore.BuiltInKey] is JsonArray importedOverrides)
        {
            var raw = await Scalar(c, tx, "SELECT body_json FROM user_settings WHERE singleton=1", token) as string;
            var local = JsonNode.Parse(raw ?? "{}")!.AsObject();
            var merged = CustomWordCategoryStore.ReadBuiltIn(raw).Concat(CustomWordCategoryStore.ReadBuiltIn(new JsonObject
                { [CustomWordCategoryStore.BuiltInKey] = importedOverrides.DeepClone() }.ToJsonString())).DistinctBy(x => x.Id).ToArray();
            local[CustomWordCategoryStore.BuiltInKey] = new JsonArray(merged.Select(x => (JsonNode?)new JsonObject
                { ["id"] = x.Id, ["name"] = x.Name, ["hidden"] = x.Hidden }).ToArray());
            await Execute(c, tx, "INSERT INTO user_settings VALUES(1,1,1,$body) ON CONFLICT(singleton) DO UPDATE SET row_revision=row_revision+1,body_json=$body", token, ("$body", local.ToJsonString()));
        }
        // Bookmarks are user data, so restore them even when local UI/audio settings are kept.
        if (plan.Package.Collections.DictionaryBookmarks is { Count: > 0 } bookmarks)
        {
            var raw = await Scalar(c, tx, "SELECT body_json FROM user_settings WHERE singleton=1", token) as string;
            var existing = DictionaryBookmarkStore.Read(raw);
            var merged = existing.Concat(bookmarks).DistinctBy(b => (b.Provider, b.EntryId)).ToArray();
            DictionaryBookmarkStore.Validate(merged);
            var local = JsonNode.Parse(raw ?? "{}")!.AsObject();
            local[DictionaryBookmarkStore.Key] = JsonSerializer.SerializeToNode(merged, ContentJson.Options);
            await Execute(c, tx, "INSERT INTO user_settings VALUES(1,1,1,$body) ON CONFLICT(singleton) DO UPDATE SET row_revision=row_revision+1,body_json=$body", token, ("$body", local.ToJsonString()));
        }
        if (plan.ApplySettings)
        {
            var local = JsonNode.Parse(await Scalar(c, tx, "SELECT body_json FROM user_settings WHERE singleton=1", token) as string ?? "{}")!.AsObject();
            var saved = plan.Package.Settings; local["uiLanguage"] = saved["uiLanguage"]!.DeepClone();
            var scale = saved["reading"]!["scalePercent"]?.GetValue<int>() / 100.0 ?? Math.Clamp(saved["reading"]!["fontSize"]!.GetValue<int>() / 20.0, 1, 2);
            local["reading"] = new JsonObject { ["showPinyin"] = saved["reading"]!["showPinyin"]!.GetValue<bool>(), ["scale"] = scale };
            local["audio"] = saved["audio"]!.DeepClone();
            var last = saved["favorites"]!["lastFolderId"]?.GetValue<string>();
            local["favorites"] = new JsonObject { ["lastFolderId"] = last is null ? null : folderMap[Guid.Parse(last)].ToString() };
            await Execute(c, tx, "INSERT INTO user_settings VALUES(1,1,1,$body) ON CONFLICT(singleton) DO UPDATE SET row_revision=row_revision+1,body_json=$body", token, ("$body", local.ToJsonString()));
        }
    }
    private static string FolderHash(BackupFolder f) => PackageJson.Hash(JsonSerializer.SerializeToNode(f, ContentJson.Options)!);
}
