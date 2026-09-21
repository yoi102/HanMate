using System.Text.Json;
using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Core.Contracts;

namespace HanMate.Infrastructure.Database;

public sealed record DictionaryBookmark(string Provider, Guid EntryId, string Title);

/// <summary>Versioned local references, never copies of dictionary definitions or publisher content.</summary>
public sealed class DictionaryBookmarkStore(VersionedLocalStateStore state)
{
    public const string Key = "dictionaryBookmarks";
    public const string Xinhua = "chinese-xinhua";
    public const string Pinyin = "bundled-pinyin";
    public const int Limit = 10000;

    public static IReadOnlyList<DictionaryBookmark> Read(string? settings)
    {
        var root = JsonNode.Parse(settings ?? "{}")!.AsObject();
        var entries = root[Key]?.Deserialize<DictionaryBookmark[]>(ContentJson.Options) ?? [];
        Validate(entries); return entries;
    }
    public static void Validate(IReadOnlyList<DictionaryBookmark> entries)
    {
        if (entries.Count > Limit || entries.Any(e => e is null || e.Provider is not (Xinhua or Pinyin) || e.EntryId == Guid.Empty
            || string.IsNullOrWhiteSpace(e.Title) || e.Title.Length > 300)
            || entries.Select(e => (e.Provider, e.EntryId)).Distinct().Count() != entries.Count)
            throw new InvalidDataException("Invalid dictionary bookmarks.");
    }
    public Task<IReadOnlyList<DictionaryBookmark>> GetAsync(CancellationToken token = default) => LoadAsync(token);
    private async Task<IReadOnlyList<DictionaryBookmark>> LoadAsync(CancellationToken token) => Read((await state.GetSettingsAsync(token))?.BodyJson);

    public async Task SetAsync(DictionaryBookmark entry, bool saved, CancellationToken token = default)
    {
        Validate([entry]);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = await state.GetSettingsAsync(token);
            var entries = Read(snapshot?.BodyJson).ToList();
            var index = entries.FindIndex(e => e.Provider == entry.Provider && e.EntryId == entry.EntryId);
            if (saved && index >= 0 || !saved && index < 0) return;
            if (saved) entries.Add(entry); else entries.RemoveAt(index);
            Validate(entries);
            var root = JsonNode.Parse(snapshot?.BodyJson ?? "{}")!.AsObject();
            root[Key] = JsonSerializer.SerializeToNode(entries, ContentJson.Options);
            try { await state.SaveSettingsAsync(root.ToJsonString(), snapshot?.RowRevision ?? 0, token); return; }
            catch (RevisionConflictException) when (attempt < 3) { }
        }
    }
}
