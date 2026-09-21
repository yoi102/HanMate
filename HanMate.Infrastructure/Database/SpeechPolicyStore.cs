using System.Text.Json.Nodes;
using HanMate.Core.Contracts;

namespace HanMate.Infrastructure.Database;

/// <summary>Device voice IDs are deliberately not persisted or backed up. Unknown settings fail closed.</summary>
public sealed class SpeechPolicyStore(VersionedLocalStateStore settings)
{
    public async Task<bool> IsAllowedAsync(CancellationToken token = default)
    {
        var snapshot = await settings.GetSettingsAsync(token);
        try { return JsonNode.Parse(snapshot?.BodyJson ?? "{}")?["audio"]?["policy"]?.GetValue<string>() == "localWithSystemFallback"; }
        catch (System.Text.Json.JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    public async Task SetAllowedAsync(bool allowed)
    {
        for (var attempt = 0; ; attempt++)
        {
            var snapshot = await settings.GetSettingsAsync(); var root = JsonNode.Parse(snapshot?.BodyJson ?? "{}")!.AsObject();
            root["audio"] = new JsonObject { ["policy"] = allowed ? "localWithSystemFallback" : "localOnly", ["preferredLocale"] = "zh-CN" };
            try { await settings.SaveSettingsAsync(root.ToJsonString(), snapshot?.RowRevision ?? 0); return; }
            catch (RevisionConflictException) when (attempt < 2) { }
        }
    }
}
