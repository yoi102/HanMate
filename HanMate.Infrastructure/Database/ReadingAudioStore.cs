using System.Text.Json;
using HanMate.Core.Audio;
using HanMate.Core.Content;
using HanMate.Core.Reading;

namespace HanMate.Infrastructure.Database;

public sealed record ReadingAudioStep(Guid TargetId, Guid UnitId, string Text, string AssetKey);
public sealed record ReadingAudioPlan(IReadOnlyList<ReadingAudioStep> Steps, IReadOnlyList<Guid> MissingTargets)
{
    public ReadingAudioPlan WithWordRecordings(ReadingDocument document, WordRecordingCatalog catalog)
    {
        if (document.Content.Kind != ContentKind.Word) return this;
        var steps = Steps.Select(step =>
        {
            if (!step.AssetKey.StartsWith("speech:", StringComparison.Ordinal) || step.TargetId != step.UnitId) return step;
            var unit = document.Content.TextUnits.Single(u => u.Id == step.UnitId);
            return catalog.Find(unit) is { } asset
                ? step with { AssetKey = $"recorded-word:{asset.Key}:{step.AssetKey}" } : step;
        }).ToArray();
        var recorded = steps.Where(s => s.AssetKey.StartsWith("recorded-word:", StringComparison.Ordinal)).Select(s => s.TargetId).ToHashSet();
        return new(steps, MissingTargets.Where(id => !recorded.Contains(id)).ToArray());
    }
}

/// <summary>A read snapshot resolves exact saved targets, never titles or visual line numbers.</summary>
public sealed class ReadingAudioStore(HanMateDatabase database)
{
    public async Task<ReadingAudioPlan> PlanAsync(ReadingDocument document, ReadingTarget? selected,
        CancellationToken token = default, bool allowSpeechFallback = false)
    {
        if (selected is not null && !document.Targets.Contains(selected)) throw new ArgumentException("Unknown reading target.");
        await database.InitializeAsync(token);
        await using var c = await database.OpenConnectionAsync(token);
        using var tx = c.BeginTransaction(deferred: true);
        using var command = c.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT body_json FROM content c WHERE c.id=$id AND " + ContentTrashStore.Active;
        command.Parameters.AddWithValue("$id", document.Content.Id.ToString());
        var current = await command.ExecuteScalarAsync(token) as string;
        if (current is null || JsonSerializer.Serialize(JsonSerializer.Deserialize<ContentDocument>(current, ContentJson.Options), ContentJson.Options)
            != JsonSerializer.Serialize(document.Content, ContentJson.Options))
            throw new InvalidOperationException("Reading content changed. Reopen the current content.");

        command.CommandText = """
            SELECT t.id,t.text_hash,t.pronunciation_hash,EXISTS(
              SELECT 1 FROM audio_binding b LEFT JOIN audio_preference p ON p.target_id=b.target_id
              WHERE b.target_id=t.id AND b.review_state='confirmed'
              AND b.bound_text_hash=t.text_hash AND b.bound_pronunciation_hash=t.pronunciation_hash
              AND (p.binding_id=b.id OR b.source_role='standard' OR c.origin='personal'))
            FROM playback_target t JOIN content c ON c.id=t.content_id WHERE c.id=$id
            """;
        var available = new Dictionary<Guid, string>(); var speech = new Dictionary<Guid, string>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
            {
                speech.Add(Guid.Parse(reader.GetString(0)), $"speech:checked:{reader.GetString(0)}:{document.Content.Id}:{reader.GetString(1)}:{reader.GetString(2)}");
                if (reader.GetBoolean(3)) available.Add(Guid.Parse(reader.GetString(0)),
                    $"local:checked:{reader.GetString(0)}:{document.Content.Id}:{reader.GetString(1)}:{reader.GetString(2)}");
            }
        var steps = new List<ReadingAudioStep>(); var missing = new List<Guid>();
        if (selected is not null) Add(selected);
        else if (document.Content.Kind is ContentKind.Word or ContentKind.Grammar)
            foreach (var target in document.Targets) Add(target);
        else
            foreach (var unit in document.Content.TextUnits)
            {
                token.ThrowIfCancellationRequested();
                var segments = unit.Segments.Where(s => s.Kind == SegmentKind.Speech).ToArray();
                if (segments.Length == 0) continue;
                // A full-unit recording has no segment timestamps. Never slice or estimate its timeline.
                if (available.ContainsKey(unit.Id)) Add(new(unit.Id, null));
                else foreach (var segment in segments) Add(new(unit.Id, segment.Id));
            }
        token.ThrowIfCancellationRequested();
        return new(steps.ToArray(), missing.ToArray());

        void Add(ReadingTarget target)
        {
            var id = target.SegmentId ?? target.UnitId;
            if (available.TryGetValue(id, out var key))
                steps.Add(new(id, target.UnitId, document.Segment(target)?.Text ?? document.Unit(target).Text, key));
            else
            {
                missing.Add(id);
                if (allowSpeechFallback)
                {
                    if (!speech.TryGetValue(id, out var keySpeech)) throw new InvalidOperationException("Speech target projection is missing.");
                    steps.Add(new(id, target.UnitId, document.Segment(target)?.Text ?? document.Unit(target).Text, keySpeech));
                }
            }
        }
    }

    public async Task<string> ReadSpeechAsync(string key, CancellationToken token = default)
    {
        var parts = key.Split(':');
        if (parts.Length != 6 || parts[0] != "speech" || parts[1] != "checked"
            || !Guid.TryParse(parts[2], out var targetId) || !Guid.TryParse(parts[3], out var contentId)) throw new InvalidDataException("Invalid speech target.");
        await database.InitializeAsync(token); using var c = await database.OpenConnectionAsync(token); using var tx = c.BeginTransaction(deferred: true);
        using var command = c.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT c.body_json FROM content c JOIN playback_target t ON t.content_id=c.id WHERE c.id=$content AND t.id=$target AND t.text_hash=$text AND t.pronunciation_hash=$pron AND " + ContentTrashStore.Active;
        command.Parameters.AddWithValue("$content", contentId.ToString()); command.Parameters.AddWithValue("$target", targetId.ToString());
        command.Parameters.AddWithValue("$text", parts[4]); command.Parameters.AddWithValue("$pron", parts[5]);
        var json = await command.ExecuteScalarAsync(token) as string ?? throw new InvalidOperationException("Speech target changed.");
        var doc = JsonSerializer.Deserialize<ContentDocument>(json, ContentJson.Options)!;
        var hashes = Packages.PackageAudio.Targets([doc]);
        if (!hashes.TryGetValue(targetId, out var hash) || hash.Text != parts[4] || hash.Pronunciation != parts[5]) throw new InvalidOperationException("Speech target changed.");
        foreach (var unit in doc.TextUnits)
        {
            if (unit.Id == targetId) return unit.Text;
            if (unit.Segments.FirstOrDefault(s => s.Id == targetId && s.Kind == SegmentKind.Speech) is { } segment) return segment.Text;
        }
        throw new InvalidOperationException("Speech target is missing.");
    }
}
