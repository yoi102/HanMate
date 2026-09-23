using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Infrastructure.Database;
using static HanMate.Infrastructure.Packages.BackupSnapshot;
using static HanMate.Infrastructure.Packages.BackupPackageCodec;

namespace HanMate.Infrastructure.Packages;

public sealed class BackupExportPlan
{
    internal BackupExportPlan(BackupPackage package, int drafts, int trash) { Package = package; Drafts = drafts; Trash = trash; }
    internal BackupPackage Package { get; }
    public DateTimeOffset CapturedAt { get; } = DateTimeOffset.UtcNow;
    public int Contents => Package.Content.Documents.Count;
    public int Folders => Package.Collections.Folders.Count;
    public int Favorites => Package.Collections.Items.Count + (Package.Collections.DictionaryBookmarks?.Count ?? 0);
    public int Audio => Package.Content.Audio.Bindings.Count;
    public long AudioBytes => Package.Content.Media.Values.Sum(b => (long)b.Length);
    public int Drafts { get; }
    public int Trash { get; }
    public int IncludedResources => Package.Resources.Resources.Count(r => r.PayloadStatus == "included");
    public IReadOnlyList<string> MissingResources => Package.Resources.Resources.Where(r => r.PayloadStatus != "included")
        .Select(r => r.Descriptor["names"]!["zh-Hans"]!.GetValue<string>() + " · " + r.Version).ToArray();
}

public sealed class BackupExportStore(HanMateDatabase database)
{
    public async Task<BackupExportPlan> PreviewAsync(CancellationToken token = default)
    {
        await new FavoriteStore(database).GetFoldersAsync(token);
        var leases = new List<Guid>(); var files = new Dictionary<string, (PackageAsset Asset, string Relative)>();
        BackupPackage package; int drafts, trash;
        try
        {
            using (var c = await database.OpenConnectionAsync(token))
            using (var tx = c.BeginTransaction())
            {
                var documents = new List<ContentDocument>(); long documentBytes = 0;
                using (var cmd = Command(c, tx, "SELECT c.body_json FROM content c WHERE " + ContentTrashStore.Active + " ORDER BY c.id LIMIT 10001"))
                using (var r = await cmd.ExecuteReaderAsync(token))
                    while (await r.ReadAsync(token))
                    {
                        var json = r.GetString(0); documentBytes += System.Text.Encoding.UTF8.GetByteCount(json);
                        Require(documentBytes <= 16L * 1024 * 1024, "PACKAGE_LIMIT_EXCEEDED");
                        documents.Add(JsonSerializer.Deserialize<ContentDocument>(json, ContentJson.Options)!);
                    }
                Require(documents.Count <= 10000, "PACKAGE_LIMIT_EXCEEDED");
                drafts = Convert.ToInt32(await Scalar(c, tx, "SELECT count(*) FROM draft WHERE COALESCE(json_extract(body_json,'$.format'),'')<>'contentTrash.v1'", token));
                trash = Convert.ToInt32(await Scalar(c, tx, "SELECT count(*) FROM draft WHERE json_extract(body_json,'$.format')='contentTrash.v1'", token));
                using (var cmd = Command(c, tx, "SELECT body_json FROM draft WHERE draft_kind='audio'"))
                using (var r = await cmd.ExecuteReaderAsync(token))
                    while (await r.ReadAsync(token)) Require(LocalAudioStore.Deserialize(r.GetString(0)).Phase != "pending", "BACKUP_RECORDING_BUSY");

                var folders = new List<BackupFolder>(); var favorites = new List<BackupFavorite>();
                using (var cmd = Command(c, tx, "SELECT id,name,description,system_role FROM favorite_folder ORDER BY sort_order,id"))
                using (var r = await cmd.ExecuteReaderAsync(token))
                    while (await r.ReadAsync(token)) folders.Add(new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), folders.Count, r.IsDBNull(3) ? null : r.GetString(3)));
                using (var cmd = Command(c, tx, "SELECT f.folder_id,f.content_id,f.added_at_utc FROM favorite_item f JOIN content c ON c.id=f.content_id WHERE " + ContentTrashStore.Active + " ORDER BY f.folder_id,f.sort_order,f.content_id"))
                using (var r = await cmd.ExecuteReaderAsync(token))
                    while (await r.ReadAsync(token)) favorites.Add(new(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), favorites.Count, DateTimeOffset.Parse(r.GetString(2)).UtcDateTime.ToString("O")));

                var assets = new Dictionary<Guid, PackageAsset>(); var bindings = new List<PackageBinding>(); var preferences = new List<PackagePreference>();
                using (var cmd = Command(c, tx, """
                    SELECT b.id,b.target_id,b.asset_id,b.bound_text_hash,b.bound_pronunciation_hash,b.review_state,b.source_role,b.label,
                      a.sha256,a.byte_length,a.duration_ms,a.sample_rate,a.channels,a.origin,a.container,a.codec,a.relative_path,p.binding_id,
                      t.text_hash,t.pronunciation_hash
                    FROM audio_binding b JOIN playback_target t ON t.id=b.target_id JOIN content c ON c.id=t.content_id
                    JOIN audio_asset a ON a.id=b.asset_id LEFT JOIN audio_preference p ON p.target_id=t.id WHERE
                    """ + " " + ContentTrashStore.Active + " ORDER BY b.id LIMIT 1001"))
                using (var r = await cmd.ExecuteReaderAsync(token))
                    while (await r.ReadAsync(token))
                    {
                        Require(r.GetString(14) == "wav" && r.GetString(15) == "pcm_s16le", "BACKUP_AUDIO_UNSUPPORTED");
                        var a = new PackageAsset(Guid.Parse(r.GetString(2)), r.GetString(13), "package", r.GetString(8), r.GetInt64(9), r.GetInt64(10),
                            "wav", "pcm_s16le", r.GetInt32(11), r.GetInt32(12), "audio/" + r.GetString(8) + ".wav");
                        assets[a.Id] = a; files.TryAdd(a.Path, (a, r.GetString(16)));
                        var review = r.GetString(5) == "confirmed" && r.GetString(3) == r.GetString(18) && r.GetString(4) == r.GetString(19) ? "confirmed" : "needsReview";
                        var b = new PackageBinding(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), a.Id, r.GetString(3), r.GetString(4), review, r.GetString(6), r.GetString(7));
                        bindings.Add(b); if (!r.IsDBNull(17) && r.GetString(17) == b.Id.ToString()) preferences.Add(new(b.TargetId, b.Id));
                    }
                Require(bindings.Count <= 1000, "PACKAGE_LIMIT_EXCEEDED");
                // No saved user recording may fall outside a serializable content graph.
                var userCount = Convert.ToInt32(await Scalar(c, tx, "SELECT count(*) FROM audio_binding b JOIN audio_asset a ON a.id=b.asset_id WHERE a.origin='user' AND NOT EXISTS(SELECT 1 FROM playback_target t JOIN content c ON c.id=t.content_id WHERE t.id=b.target_id AND NOT (" + ContentTrashStore.Active + "))", token));
                Require(userCount == bindings.Count(b => assets[b.AssetId].Origin == "user"), "BACKUP_AUDIO_TARGET_UNSUPPORTED");
                var audio = new PackageAudio(1, assets.Values.ToArray(), bindings, preferences);
                var resources = new List<BackupResource>(); var omitted = new HashSet<Guid>();
                var teaching = new List<JsonObject>();
                using (var cmd = Command(c,tx,"SELECT body_json FROM pinyin_item ORDER BY sort_order,id"))
                using (var r = await cmd.ExecuteReaderAsync(token)) while(await r.ReadAsync(token)) teaching.Add(PackageJson.Parse(System.Text.Encoding.UTF8.GetBytes(r.GetString(0))).AsObject());
                Require(teaching.Count <= 200,"PACKAGE_LIMIT_EXCEEDED");
                var needed = favorites.Select(f => f.ContentId).ToHashSet();
                foreach(var item in teaching) foreach(var e in item["examples"]!.AsArray()) if(e!["contentId"] is { } id) needed.Add(Guid.Parse(id.GetValue<string>()));
                var targetContents = documents.SelectMany(d => d.TextUnits.SelectMany(u => new[] { u.Id }.Concat(u.Segments.Select(s => s.Id))).Select(id => (id, d.Id))).ToDictionary(x => x.id, x => x.Id);
                foreach (var b in bindings.Where(b => assets[b.AssetId].Origin == "user")) needed.Add(targetContents[b.TargetId]);
                using (var cmd = Command(c, tx, "SELECT descriptor_json,descriptor_sha256,payload_fingerprint,is_present,enabled,priority FROM installed_resource ORDER BY resource_id"))
                using (var r = await cmd.ExecuteReaderAsync(token))
                    while (await r.ReadAsync(token))
                    {
                        var descriptor = JsonNode.Parse(r.GetString(0))!.AsObject();
                        var row = new BackupResource(descriptor, r.GetString(1), r.GetString(2), "included", r.GetBoolean(4), r.GetInt32(5), null, []);
                        var docs = documents.Where(d => row.ContentIds.Contains(d.Id)).ToArray();
                        var complete = r.GetBoolean(3) && docs.Length == row.ContentIds.Length && descriptor["backupPolicy"]!.GetValue<string>() == "allowed"
                            && docs.All(d => d.Source.BackupPolicy == BackupPolicy.Allowed);
                        if (complete) complete = PayloadFingerprint(descriptor, docs, audio) == row.PayloadFingerprint;
                        if (!complete)
                        {
                            row = row with { PayloadStatus = "referenceOnly", MissingReason = "Source unavailable, restricted, or modified; reinstall the matching resource." };
                            foreach (var d in docs)
                            {
                                if (needed.Contains(d.Id)) { Require(d.Source.BackupPolicy == BackupPolicy.Allowed, "BACKUP_RIGHTS_REQUIRED"); documents[documents.IndexOf(d)] = d with { Origin = ContentOrigin.Retained }; }
                                else omitted.Add(d.Id);
                            }
                        }
                        resources.Add(row);
                    }
                for (var i = 0; i < resources.Count; i++)
                {
                    var removed = new List<string>();
                    using var cmd = Command(c, tx, "SELECT entry_id FROM resource_entry_override WHERE resource_id=$id AND removed=1 ORDER BY entry_id", ("$id", resources[i].Id.ToString()));
                    using var r = await cmd.ExecuteReaderAsync(token); while (await r.ReadAsync(token)) removed.Add(r.GetString(0));
                    resources[i] = resources[i] with { RemovedEntryIds = removed };
                }
                documents.RemoveAll(d => omitted.Contains(d.Id));
                foreach (var d in documents.Where(d => d.Origin != ContentOrigin.Resource))
                    Require(d.Source.BackupPolicy == BackupPolicy.Allowed || d.Origin == ContentOrigin.Personal && d.Source.SourceId == "personal" && d.Source.ResourceId is null, "BACKUP_RIGHTS_REQUIRED");
                var includedTargets = PackageAudio.Targets(documents);
                bindings = bindings.Where(b => includedTargets.ContainsKey(b.TargetId)).ToList();
                var includedAssets = bindings.Select(b => b.AssetId).ToHashSet();
                assets = assets.Where(p => includedAssets.Contains(p.Key)).ToDictionary();
                preferences = preferences.Where(p => bindings.Any(b => b.Id == p.BindingId)).ToList();
                audio = new(1, assets.Values.ToArray(), bindings, preferences);
                files = files.Where(p => assets.Values.Any(a => a.Path == p.Key)).ToDictionary();
                Require(files.Values.Sum(v => v.Asset.ByteLength) <= 30L * 1024 * 1024 && files.Values.All(v => v.Asset.ByteLength <= 16L * 1024 * 1024), "PACKAGE_LIMIT_EXCEEDED");
                var rawSettings = await Scalar(c, tx, "SELECT body_json FROM user_settings WHERE singleton=1", token) as string;
                var bookmarks = DictionaryBookmarkStore.Read(rawSettings);
                var settings = ExportSettings(rawSettings, folders);
                foreach (var (_, entry) in files)
                {
                    var id = Guid.NewGuid(); leases.Add(id);
                    await Execute(c, tx, "INSERT INTO file_lease VALUES($id,$hash,'export',$expires)", token,
                        ("$id", id.ToString()), ("$hash", entry.Asset.Sha256), ("$expires", DateTimeOffset.UtcNow.AddHours(4).ToString("O")));
                }
                package = new(new(Guid.NewGuid(), "", "", documents, audio, new Dictionary<string, byte[]>(), true),
                    new(bookmarks.Count == 0 ? 1 : 2, folders, favorites, bookmarks.Count == 0 ? null : bookmarks), settings, new(1, resources, documents.Where(d => d.Origin == ContentOrigin.Retained).Select(d => d.Id).ToArray(), teaching));
                token.ThrowIfCancellationRequested(); tx.Commit();
            }
            var media = new Dictionary<string, byte[]>(); var root = Path.GetDirectoryName(database.DatabasePath)! + Path.DirectorySeparatorChar;
            foreach (var (path, entry) in files)
            {
                var full = Path.GetFullPath(Path.Combine(root, entry.Relative));
                Require(!Path.IsPathRooted(entry.Relative) && full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "BACKUP_AUDIO_INVALID");
                await using var stream = File.OpenRead(full); Require(stream.Length == entry.Asset.ByteLength, "BACKUP_AUDIO_INVALID");
                var bytes = new byte[checked((int)stream.Length)]; await stream.ReadExactlyAsync(bytes, token);
                Require(PackageJson.Hash(bytes) == entry.Asset.Sha256, "BACKUP_AUDIO_INVALID"); media.Add(path, bytes);
            }
            package = package with { Content = package.Content with { Media = media } };
            // Preview is immutable, including audio bytes. Later edits cannot mix into this snapshot.
            using var check = new MemoryStream(); await WriteAsync(package, check, token); check.Position = 0;
            package = await ReadAsync(check, token);
            return new(package, drafts, trash);
        }
        finally
        {
            if (leases.Count > 0)
            {
                using var c = await database.OpenConnectionAsync(); using var tx = c.BeginTransaction();
                foreach (var id in leases) await Execute(c, tx, "DELETE FROM file_lease WHERE lease_id=$id", CancellationToken.None, ("$id", id.ToString()));
                tx.Commit();
            }
        }
    }
    public async Task ExportAsync(BackupExportPlan plan, Stream output, bool acceptMissingResources, CancellationToken token = default)
    {
        Require(acceptMissingResources || plan.MissingResources.Count == 0, "BACKUP_PARTIAL_CONFIRMATION_REQUIRED");
        using var stage = new MemoryStream(); await WriteAsync(plan.Package with { Content = plan.Package.Content with { Id = Guid.NewGuid() } }, stage, token); stage.Position = 0;
        await ReadAsync(stage, token); stage.Position = 0; await stage.CopyToAsync(output, token);
    }
    private static JsonObject ExportSettings(string? json, IReadOnlyList<BackupFolder> folders)
    {
        var local = JsonNode.Parse(json ?? "{}")!; var scale = local["reading"]?["scale"]?.GetValue<double>() ?? 1;
        Require(double.IsFinite(scale) && scale is >= 1 and <= 2, "BACKUP_SETTINGS_INVALID");
        var last = local["favorites"]?["lastFolderId"]?.GetValue<string>();
        if (last is not null && !folders.Any(f => f.Id.ToString() == last)) last = null;
        return new JsonObject { ["schemaVersion"] = 1, ["uiLanguage"] = local["uiLanguage"]?.GetValue<string>() ?? "zh-Hans",
            ["reading"] = new JsonObject { ["showPinyin"] = local["reading"]?["showPinyin"]?.GetValue<bool>() ?? true,
                ["fontSize"] = (int)Math.Round(20 * scale), ["scalePercent"] = (int)Math.Round(100 * scale) },
            ["audio"] = local["audio"]?.DeepClone() ?? new JsonObject { ["policy"] = "localOnly", ["preferredLocale"] = "zh-CN" },
            ["favorites"] = new JsonObject { ["lastFolderId"] = last },
            [CustomWordCategoryStore.Key] = new JsonArray(CustomWordCategoryStore.Read(json).Select(x => (JsonNode?)new JsonObject
                { ["id"] = x.Id, ["name"] = x.Name }).ToArray()),
            [CustomWordCategoryStore.BuiltInKey] = new JsonArray(CustomWordCategoryStore.ReadBuiltIn(json).Select(x => (JsonNode?)new JsonObject
                { ["id"] = x.Id, ["name"] = x.Name, ["hidden"] = x.Hidden }).ToArray()) };
    }
}
