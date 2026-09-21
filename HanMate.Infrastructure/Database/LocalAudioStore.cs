using System.Security.Cryptography;
using System.Text.Json;
using HanMate.Core.Audio;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed record AudioTarget(Guid Id, Guid ContentId, string TextHash, string PronunciationHash, bool Personal);
public sealed record AudioDraft(Guid Id, AudioTarget Target, string SourceRole, string Phase, string? Sha256 = null, WaveInfo? Wave = null);
public sealed record AudioTrack(Guid Id, string Label, string SourceRole, bool Eligible, bool Preferred, long DurationMs);

/// <summary>
/// Draft row is the write-ahead intent: pending -> file flushed/renamed -> ready -> binding transaction.
/// Recovery validates complete files only. Discarded bytes enter the separate, protected orphan cleanup workflow.
/// </summary>
public sealed class LocalAudioStore(HanMateDatabase database)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Root => Path.GetDirectoryName(database.DatabasePath)!;
    public string CapturePath(Guid id) => Path.Combine(Root, "audio", "local", id.ToString("D") + ".part");
    private string Relative(Guid id) => "audio/local/" + id.ToString("D") + ".wav";
    private string ReadyPath(Guid id) => Path.Combine(Root, Relative(id));

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] parameters)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private async Task<SqliteConnection> OpenAsync()
    { await database.InitializeAsync(); return await database.OpenConnectionAsync(); }

    public async Task<AudioTarget> GetTargetAsync(Guid targetId)
    { await using var connection = await OpenAsync(); return await TargetAsync(connection, null, targetId); }

    private static async Task<AudioTarget> TargetAsync(SqliteConnection c, SqliteTransaction? tx, Guid id)
    {
        await using var command = Command(c, tx, "SELECT t.content_id,t.text_hash,t.pronunciation_hash,c.origin FROM playback_target t JOIN content c ON c.id=t.content_id WHERE t.id=$id AND " + ContentTrashStore.Active, ("$id", id.ToString()));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Audio target no longer exists.");
        return new(id, Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3) == "personal");
    }
    private static async Task CheckTargetAsync(SqliteConnection c, SqliteTransaction tx, AudioTarget expected)
    {
        if (await TargetAsync(c, tx, expected.Id) != expected) throw new InvalidOperationException("Audio target changed. Review the current text first.");
    }

    public async Task<AudioDraft> BeginAsync(AudioTarget target, string sourceRole)
    {
        if (sourceRole is not ("user" or "imported")) throw new ArgumentException("Invalid audio source.");
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.Combine(Root, "audio", "local"));
            await using var c = await OpenAsync(); using var tx = c.BeginTransaction();
            await CheckTargetAsync(c, tx, target);
            var draft = new AudioDraft(Guid.NewGuid(), target, sourceRole, "pending");
            await using var insert = Command(c, tx, "INSERT INTO draft(id,target_content_id,draft_kind,body_json,updated_at_utc) VALUES($id,$content,'audio',$body,$time)",
                ("$id", draft.Id.ToString()), ("$content", target.ContentId.ToString()), ("$body", Serialize(draft)), ("$time", DateTimeOffset.UtcNow.ToString("O")));
            await insert.ExecuteNonQueryAsync(); tx.Commit(); return draft;
        }
        finally { _gate.Release(); }
    }

    public async Task<AudioDraft> ImportAsync(AudioTarget target, Stream input, CancellationToken cancellationToken = default)
    {
        var draft = await BeginAsync(target, "imported");
        await using (var output = new FileStream(CapturePath(draft.Id), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
            {
                total += read;
                if (total > PcmWave.MaximumBytes) throw new InvalidDataException("Audio exceeds 100 MiB.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            output.Flush(true);
        }
        return await FinalizeAsync(draft.Id);
    }

    public async Task<AudioDraft> FinalizeAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            await using var c = await OpenAsync();
            await using var read = Command(c, null, "SELECT body_json FROM draft WHERE id=$id AND draft_kind='audio'", ("$id", id.ToString()));
            var original = await read.ExecuteScalarAsync() as string ?? throw new InvalidOperationException("Audio draft no longer exists.");
            var draft = Deserialize(original);
            if (draft.Phase == "ready") return draft;
            var path = File.Exists(ReadyPath(id)) ? ReadyPath(id) : File.Exists(CapturePath(id)) ? CapturePath(id) : CapturePath(id) + ".wav";
            WaveInfo wave; string hash;
            await using (var input = File.OpenRead(path))
            { wave = PcmWave.Inspect(input); hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input)); }
            if (draft.SourceRole == "user" && wave.DurationMs > 1_200_000) throw new InvalidDataException("Recording exceeds 20 minutes.");
            if (path != ReadyPath(id)) File.Move(path, ReadyPath(id));
            draft = draft with { Phase = "ready", Sha256 = hash, Wave = wave };
            await using var update = Command(c, null, "UPDATE draft SET body_json=$body,row_revision=row_revision+1,updated_at_utc=$time WHERE id=$id AND draft_kind='audio' AND body_json=$original",
                ("$id", id.ToString()), ("$body", Serialize(draft)), ("$original", original), ("$time", DateTimeOffset.UtcNow.ToString("O")));
            if (await update.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("Draft changed during finalization. Reload it before retrying.");
            return draft;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<AudioDraft>> ListDraftsAsync(Guid targetId)
    {
        // Only recover this target's pending files after capture has stopped (caller owns coordinator session).
        await using var c = await OpenAsync();
        var drafts = new List<AudioDraft>();
        await using (var command = Command(c, null, "SELECT body_json FROM draft WHERE draft_kind='audio' ORDER BY updated_at_utc DESC"))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var draft = Deserialize(reader.GetString(0));
                if (draft.Target.Id == targetId) drafts.Add(draft);
            }
        return drafts;
    }

    public async Task<Guid> SaveAsync(AudioDraft expected, string label, bool makeDefault)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 120) throw new ArgumentException("Audio label must contain 1–120 characters.");
        await _gate.WaitAsync();
        try
        {
            await using var c = await OpenAsync(); using var tx = c.BeginTransaction();
            var draft = await DraftAsync(c, tx, expected.Id);
            if (draft != expected || draft.Phase != "ready" || draft.Wave is null || draft.Sha256 is null) throw new InvalidOperationException("Draft changed or is incomplete.");
            await CheckTargetAsync(c, tx, draft.Target);
            // Validate the exact immutable bytes before promoting the file to a saved asset.
            await using var file = File.OpenRead(ReadyPath(draft.Id));
            if (PcmWave.Inspect(file) != draft.Wave || Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) != draft.Sha256)
                throw new InvalidDataException("Audio file was changed.");
            var assetId = Guid.NewGuid(); var bindingId = Guid.NewGuid();
            await using var insert = Command(c, tx, """
                INSERT INTO audio_asset(id,sha256,relative_path,byte_length,duration_ms,container,codec,sample_rate,channels,origin)
                VALUES($asset,$hash,$path,$length,$duration,'wav','pcm_s16le',$rate,$channels,'user');
                INSERT INTO audio_binding(id,target_id,asset_id,bound_text_hash,bound_pronunciation_hash,review_state,source_role,label)
                VALUES($binding,$target,$asset,$text,$pronunciation,'confirmed',$source,$label);
                DELETE FROM draft WHERE id=$draft;
                UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1;
                """, ("$asset", assetId.ToString()), ("$hash", draft.Sha256), ("$path", Relative(draft.Id)), ("$length", file.Length),
                ("$duration", draft.Wave.DurationMs), ("$rate", draft.Wave.SampleRate), ("$channels", draft.Wave.Channels),
                ("$binding", bindingId.ToString()), ("$target", draft.Target.Id.ToString()), ("$text", draft.Target.TextHash),
                ("$pronunciation", draft.Target.PronunciationHash), ("$source", draft.SourceRole), ("$label", label.Trim()), ("$draft", draft.Id.ToString()));
            await insert.ExecuteNonQueryAsync();
            if (makeDefault) await PreferenceAsync(c, tx, draft.Target.Id, bindingId);
            tx.Commit(); return bindingId;
        }
        finally { _gate.Release(); }
    }

    public async Task DiscardAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            await using var c = await OpenAsync();
            await using var command = Command(c, null, "DELETE FROM draft WHERE id=$id AND draft_kind='audio'", ("$id", id.ToString()));
            await command.ExecuteNonQueryAsync();
            // OrphanAudioStore observes discarded bytes for seven days before explicit, lease-aware cleanup.
        }
        finally { _gate.Release(); }
    }

    public async Task ReviewDraftAsync(AudioDraft expected, AudioTarget current)
    {
        await _gate.WaitAsync();
        try
        {
            await using var c = await OpenAsync(); using var tx = c.BeginTransaction();
            var draft = await DraftAsync(c, tx, expected.Id);
            if (draft != expected || draft.Phase != "ready" || draft.Target.Id != current.Id) throw new InvalidOperationException("Draft changed.");
            await CheckTargetAsync(c, tx, current);
            await using var update = Command(c, tx, "UPDATE draft SET body_json=$body,row_revision=row_revision+1 WHERE id=$id", ("$id", draft.Id.ToString()), ("$body", Serialize(draft with { Target = current })));
            await update.ExecuteNonQueryAsync(); tx.Commit();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<AudioTrack>> ListTracksAsync(Guid targetId)
    {
        await using var c = await OpenAsync();
        await using var command = Command(c, null, """
            SELECT b.id,b.label,b.source_role,
              b.review_state='confirmed' AND b.bound_text_hash=t.text_hash AND b.bound_pronunciation_hash=t.pronunciation_hash,
              p.binding_id=b.id,a.duration_ms
            FROM audio_binding b JOIN playback_target t ON t.id=b.target_id JOIN audio_asset a ON a.id=b.asset_id
            LEFT JOIN audio_preference p ON p.target_id=t.id WHERE t.id=$id ORDER BY b.source_role,b.label,b.id
            """, ("$id", targetId.ToString()));
        var result = new List<AudioTrack>(); await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), !reader.IsDBNull(4) && reader.GetBoolean(4), reader.GetInt64(5)));
        return result;
    }

    public async Task UpdateTrackAsync(AudioTarget expected, Guid bindingId, string action)
    {
        if (action is not ("confirm" or "default" or "remove")) throw new ArgumentException("Unknown audio action.");
        await _gate.WaitAsync();
        try
        {
            await using var c = await OpenAsync(); using var tx = c.BeginTransaction();
            await CheckTargetAsync(c, tx, expected);
            await using var get = Command(c, tx, "SELECT source_role,review_state,bound_text_hash,bound_pronunciation_hash FROM audio_binding WHERE id=$binding AND target_id=$target",
                ("$binding", bindingId.ToString()), ("$target", expected.Id.ToString()));
            bool eligible; string role;
            await using (var r = await get.ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) throw new InvalidOperationException("Track no longer exists.");
                role = r.GetString(0); eligible = r.GetString(1) == "confirmed" && r.GetString(2) == expected.TextHash && r.GetString(3) == expected.PronunciationHash;
            }
            if (action == "default")
            {
                if (!eligible) throw new InvalidOperationException("Review this track first.");
                await PreferenceAsync(c, tx, expected.Id, bindingId);
            }
            else
            {
                if (role == "standard") throw new InvalidOperationException("Standard tracks are managed by their resource.");
                await using var change = Command(c, tx, action == "remove" ? "DELETE FROM audio_binding WHERE id=$binding" :
                    "UPDATE audio_binding SET review_state='confirmed',bound_text_hash=$text,bound_pronunciation_hash=$pronunciation WHERE id=$binding",
                    ("$binding", bindingId.ToString()), ("$text", expected.TextHash), ("$pronunciation", expected.PronunciationHash));
                await change.ExecuteNonQueryAsync();
            }
            await using var epoch = Command(c, tx, "UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1");
            await epoch.ExecuteNonQueryAsync(); tx.Commit();
        }
        finally { _gate.Release(); }
    }

    private static async Task PreferenceAsync(SqliteConnection c, SqliteTransaction tx, Guid target, Guid binding)
    {
        await using var command = Command(c, tx, "INSERT INTO audio_preference(target_id,binding_id) VALUES($target,$binding) ON CONFLICT(target_id) DO UPDATE SET binding_id=excluded.binding_id", ("$target", target.ToString()), ("$binding", binding.ToString()));
        await command.ExecuteNonQueryAsync();
    }

    // Automatic routing rechecks eligibility at playback time; explicit audition may include needsReview.
    public Task<AudioReadLease> OpenPlaybackAsync(string kind, Guid id) => OpenAudioAsync(kind, id, "playback");
    public Task<AudioReadLease> OpenExpectedPlaybackAsync(Guid id, Guid contentId, string textHash, string pronunciationHash)
        => OpenAudioAsync("target", id, "playback", (contentId, textHash, pronunciationHash));
    internal Task<AudioReadLease> OpenExportAsync(Guid bindingId) => OpenAudioAsync("track", bindingId, "export");

    public async Task ExportRecordingAsync(AudioTarget expected, Guid bindingId, Stream output, CancellationToken token = default)
    {
        await using (var c = await OpenAsync())
        {
            using var tx = c.BeginTransaction(deferred: true); await CheckTargetAsync(c, tx, expected);
            using var command = Command(c, tx, "SELECT count(*) FROM audio_binding b JOIN audio_asset a ON a.id=b.asset_id WHERE b.id=$id AND b.target_id=$target AND b.source_role='user' AND a.origin='user'", ("$id", bindingId.ToString()), ("$target", expected.Id.ToString()));
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1) throw new InvalidOperationException("Only saved user recordings can be shared.");
        }
        await using var lease = await OpenAudioAsync("track", bindingId, "export");
        await lease.Stream.CopyToAsync(output, token);
    }

    private async Task<AudioReadLease> OpenAudioAsync(string kind, Guid id, string reason,
        (Guid ContentId, string TextHash, string PronunciationHash)? expected = null)
    {
        await using var c = await OpenAsync(); using var tx = c.BeginTransaction();
        if (expected is { } snapshot)
        {
            var target = await TargetAsync(c, tx, id);
            if (target.ContentId != snapshot.ContentId || target.TextHash != snapshot.TextHash || target.PronunciationHash != snapshot.PronunciationHash)
                throw new InvalidOperationException("Reading audio target changed.");
        }
        string relative, hash; long duration;
        if (kind == "draft")
        {
            var draft = await DraftAsync(c, tx, id);
            if (draft.Phase != "ready" || draft.Wave is null || draft.Sha256 is null) throw new InvalidDataException("Draft is incomplete.");
            relative = Relative(id); hash = draft.Sha256; duration = draft.Wave.DurationMs;
        }
        else
        {
            if (kind is not ("track" or "target")) throw new ArgumentException("Invalid playback request.");
            await using var query = Command(c, tx, """
                SELECT a.relative_path,a.sha256,a.duration_ms FROM audio_binding b JOIN audio_asset a ON a.id=b.asset_id
                JOIN playback_target t ON t.id=b.target_id JOIN content c ON c.id=t.content_id
                LEFT JOIN audio_preference p ON p.target_id=t.id
                WHERE
                """ + " " + ContentTrashStore.Active + " AND " + (kind == "track" ? "b.id=$id" : """
                t.id=$id AND b.review_state='confirmed' AND b.bound_text_hash=t.text_hash AND b.bound_pronunciation_hash=t.pronunciation_hash
                AND (p.binding_id=b.id OR b.source_role='standard' OR c.origin='personal')
                ORDER BY CASE WHEN p.binding_id=b.id THEN 0 WHEN b.source_role='standard' THEN 1 ELSE 2 END,b.id LIMIT 1
                """), ("$id", id.ToString()));
            await using var reader = await query.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("No applicable audio track.");
            relative = reader.GetString(0); hash = reader.GetString(1); duration = reader.GetInt64(2);
        }
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (Path.IsPathRooted(relative) || !full.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Unsafe audio path.");
        var leaseId = Guid.NewGuid();
        await using var lease = Command(c, tx, "INSERT INTO file_lease(lease_id,sha256,reason,expires_at_utc) VALUES($id,$hash,$reason,$expires)",
            ("$id", leaseId.ToString()), ("$hash", hash), ("$reason", reason), ("$expires", DateTimeOffset.UtcNow.AddHours(4).ToString("O")));
        await lease.ExecuteNonQueryAsync(); tx.Commit();
        FileStream? stream = null;
        try
        {
            stream = File.OpenRead(full);
            var wave = PcmWave.Inspect(stream);
            if (wave.DurationMs != duration) throw new InvalidDataException("Audio metadata mismatch.");
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)) != hash) throw new InvalidDataException("Audio checksum mismatch.");
            stream.Position = 0;
            return new(stream, duration, async () => await ReleaseLeaseAsync(leaseId));
        }
        catch { if (stream is not null) await stream.DisposeAsync(); await ReleaseLeaseAsync(leaseId); throw; }
    }
    private async Task ReleaseLeaseAsync(Guid id)
    {
        await using var c = await OpenAsync();
        await using var command = Command(c, null, "DELETE FROM file_lease WHERE lease_id=$id", ("$id", id.ToString())); await command.ExecuteNonQueryAsync();
    }
    internal static string Serialize(AudioDraft draft) => JsonSerializer.Serialize(new Envelope("localAudio.v1", draft));
    internal static AudioDraft Deserialize(string json, bool strict = false)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, strict ? MigrationJson : null);
        if (envelope?.Format != "localAudio.v1" || envelope.Draft is null) throw new InvalidDataException("Unknown audio draft format.");
        return envelope.Draft;
    }
    private static readonly JsonSerializerOptions MigrationJson = new() { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private sealed record Envelope(string Format, AudioDraft Draft);
    private static async Task<AudioDraft> DraftAsync(SqliteConnection c, SqliteTransaction? tx, Guid id)
    {
        await using var command = Command(c, tx, "SELECT body_json FROM draft WHERE id=$id AND draft_kind='audio'", ("$id", id.ToString()));
        var json = await command.ExecuteScalarAsync() as string ?? throw new InvalidOperationException("Audio draft no longer exists.");
        return Deserialize(json);
    }
}

public sealed class AudioReadLease(Stream stream, long durationMs, Func<Task> release) : IAsyncDisposable
{
    public Stream Stream { get; } = stream;
    public long DurationMs { get; } = durationMs;
    public async ValueTask DisposeAsync() { try { await Stream.DisposeAsync(); } finally { await release(); } }
}
