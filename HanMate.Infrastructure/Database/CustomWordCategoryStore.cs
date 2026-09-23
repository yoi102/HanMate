using System.Text.Json.Nodes;
using HanMate.Core.Content;
using HanMate.Core.Contracts;

namespace HanMate.Infrastructure.Database;

public sealed record CustomWordCategory(string Id, string Name);
public sealed record BuiltInWordCategoryPreference(string Id, string? Name, bool Hidden);

/// <summary>Personal category identities live in backed-up user settings, separate from their word memberships.</summary>
public sealed class CustomWordCategoryStore(VersionedLocalStateStore settings)
{
    public const string Key = "wordCategories";
    public const string BuiltInKey = "wordCategoryOverrides";
    public static bool IsCustom(string id) => id.StartsWith("custom-", StringComparison.Ordinal) &&
        Guid.TryParseExact(id[7..], "N", out _);

    public async Task<IReadOnlyList<CustomWordCategory>> ListAsync(CancellationToken token = default)
    {
        var snapshot = await settings.GetSettingsAsync(token);
        return Read(snapshot?.BodyJson).ToArray();
    }

    public async Task<IReadOnlyList<BuiltInWordCategoryPreference>> ListBuiltInAsync(CancellationToken token = default)
    {
        var snapshot = await settings.GetSettingsAsync(token);
        return ReadBuiltIn(snapshot?.BodyJson);
    }

    public Task RenameBuiltInAsync(string id, string name, CancellationToken token = default)
    {
        ValidateBuiltIn(id); name = ValidateName(name);
        return ChangeBuiltInAsync(items =>
        {
            var index = items.FindIndex(x => x.Id == id);
            var current = index < 0 ? new BuiltInWordCategoryPreference(id, null, false) : items[index];
            if (index < 0) items.Add(current with { Name = name });
            else items[index] = current with { Name = name };
        }, token);
    }

    public Task SetBuiltInHiddenAsync(string id, bool hidden, CancellationToken token = default)
    {
        ValidateBuiltIn(id);
        return ChangeBuiltInAsync(items =>
        {
            var index = items.FindIndex(x => x.Id == id);
            var current = index < 0 ? new BuiltInWordCategoryPreference(id, null, false) : items[index];
            if (index < 0) items.Add(current with { Hidden = hidden });
            else items[index] = current with { Hidden = hidden };
        }, token);
    }

    public async Task<CustomWordCategory> CreateAsync(string name, CancellationToken token = default)
    {
        name = ValidateName(name);
        var category = new CustomWordCategory("custom-" + Guid.NewGuid().ToString("N"), name);
        await ChangeAsync(items =>
        {
            if (items.Count >= 100) throw new InvalidDataException("CATEGORY_LIMIT");
            if (items.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("CATEGORY_DUPLICATE");
            items.Add(category);
        }, token);
        return category;
    }

    public Task RenameAsync(string id, string name, CancellationToken token = default)
    {
        if (!IsCustom(id)) throw new ArgumentOutOfRangeException(nameof(id));
        name = ValidateName(name);
        return ChangeAsync(items =>
        {
            var index = items.FindIndex(x => x.Id == id);
            if (index < 0) throw new InvalidOperationException("CATEGORY_MISSING");
            if (items.Any(x => x.Id != id && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("CATEGORY_DUPLICATE");
            items[index] = items[index] with { Name = name };
        }, token);
    }

    public Task DeleteAsync(string id, CancellationToken token = default)
    {
        if (!IsCustom(id)) throw new ArgumentOutOfRangeException(nameof(id));
        return ChangeAsync(items =>
        {
            if (items.RemoveAll(x => x.Id == id) != 1) throw new InvalidOperationException("CATEGORY_MISSING");
        }, token);
    }

    private async Task ChangeAsync(Action<List<CustomWordCategory>> change, CancellationToken token)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = await settings.GetSettingsAsync(token);
            var root = JsonNode.Parse(snapshot?.BodyJson ?? "{}")!.AsObject();
            var items = Read(snapshot?.BodyJson).ToList();
            change(items);
            root[Key] = new JsonArray(items.Select(x => (JsonNode?)new JsonObject { ["id"] = x.Id, ["name"] = x.Name }).ToArray());
            try { await settings.SaveSettingsAsync(root.ToJsonString(), snapshot?.RowRevision ?? 0, token); return; }
            catch (RevisionConflictException) when (attempt < 2) { }
        }
    }

    private async Task ChangeBuiltInAsync(Action<List<BuiltInWordCategoryPreference>> change, CancellationToken token)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = await settings.GetSettingsAsync(token);
            var root = JsonNode.Parse(snapshot?.BodyJson ?? "{}")!.AsObject();
            var items = ReadBuiltIn(snapshot?.BodyJson).ToList();
            change(items);
            root[BuiltInKey] = new JsonArray(items.Select(x => (JsonNode?)new JsonObject
                { ["id"] = x.Id, ["name"] = x.Name, ["hidden"] = x.Hidden }).ToArray());
            try { await settings.SaveSettingsAsync(root.ToJsonString(), snapshot?.RowRevision ?? 0, token); return; }
            catch (RevisionConflictException) when (attempt < 2) { }
        }
    }

    public static IReadOnlyList<BuiltInWordCategoryPreference> ReadBuiltIn(string? json)
    {
        var root = JsonNode.Parse(json ?? "{}")!.AsObject();
        return root[BuiltInKey] is not JsonArray array ? [] : array.Select(node =>
            new BuiltInWordCategoryPreference(node!["id"]!.GetValue<string>(),
                node["name"]?.GetValue<string>(), node["hidden"]?.GetValue<bool>() ?? false))
            .Where(x => IsBuiltIn(x.Id) && (x.Name is null || x.Name.Length is > 0 and <= 40))
            .DistinctBy(x => x.Id).ToArray();
    }

    private static bool IsBuiltIn(string id) => id == WordCategories.Other || WordCategories.All.Contains(id);
    private static void ValidateBuiltIn(string id)
    {
        if (!IsBuiltIn(id)) throw new ArgumentOutOfRangeException(nameof(id));
    }

    public static IReadOnlyList<CustomWordCategory> Read(string? json)
    {
        var root = JsonNode.Parse(json ?? "{}")!.AsObject();
        return root[Key] is not JsonArray array ? [] : array.Select(node =>
            new CustomWordCategory(node!["id"]!.GetValue<string>(), node["name"]!.GetValue<string>()))
            .Where(x => IsCustom(x.Id) && !string.IsNullOrWhiteSpace(x.Name)).DistinctBy(x => x.Id).ToArray();
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl)) throw new InvalidDataException("CATEGORY_NAME_INVALID");
        return name;
    }
}
