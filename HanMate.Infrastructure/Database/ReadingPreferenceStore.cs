using System.Text.Json.Nodes;
using HanMate.Core.Contracts;

namespace HanMate.Infrastructure.Database;

public sealed record ReadingPreference(bool ShowPinyin = true, double Scale = 1);
public sealed class ReadingPreferenceStore(VersionedLocalStateStore settings)
{
    public async Task<ReadingPreference> GetAsync()
    {
        var snapshot = await settings.GetSettingsAsync();
        try
        {
            var root = JsonNode.Parse(snapshot?.BodyJson ?? "{}");
            var scale = root?["reading"]?["scale"]?.GetValue<double>() ?? 1;
            return new(root?["reading"]?["showPinyin"]?.GetValue<bool>() ?? true, double.IsFinite(scale) && scale is >= 1 and <= 2 ? scale : 1);
        }
        catch { return new(); }
    }
    public async Task SaveAsync(ReadingPreference value)
    {
        if (!double.IsFinite(value.Scale) || value.Scale is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(value));
        for (var attempt = 0; ; attempt++)
        {
            var snapshot = await settings.GetSettingsAsync(); var root = JsonNode.Parse(snapshot?.BodyJson ?? "{}")!.AsObject();
            var reading = root["reading"] as JsonObject ?? new JsonObject();
            reading["showPinyin"] = value.ShowPinyin; reading["scale"] = value.Scale; root["reading"] = reading;
            try { await settings.SaveSettingsAsync(root.ToJsonString(), snapshot?.RowRevision ?? 0); return; }
            catch (RevisionConflictException) when (attempt < 2) { }
        }
    }
}
