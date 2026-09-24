using System.Text.Json;
using HanMate.Core.Content;

namespace HanMate.Infrastructure.Database;

public sealed record LearningFilter(ContentKind Kind, Difficulty? Difficulty = null, SchoolStage? Stage = null,
    int? Grade = null, IReadOnlyList<string>? Scenes = null, bool PersonalOnly = false, string? WordCategory = null);
public sealed record LearningRow(Guid Id, string Title, string BodyJson);
public sealed record LearningResult(IReadOnlyList<LearningRow> Items, int Total, int UnfilteredTotal);
public sealed record WordCategoryCounts(int Total, IReadOnlyDictionary<string, int> Counts);

public sealed class LearningCatalogStore(HanMateDatabase database)
{
    private const string Visible = """
        (c.origin='personal' OR (r.resource_kind='learning' AND r.enabled=1 AND r.is_present=1 AND COALESCE(o.removed,0)=0))
        """ + " AND " + ContentTrashStore.Active;
    private const string Joins = """
        FROM content c LEFT JOIN resource_entry e ON e.content_id=c.id
        LEFT JOIN installed_resource r ON r.resource_id=e.resource_id
        LEFT JOIN resource_entry_override o ON o.resource_id=e.resource_id AND o.entry_id=e.entry_id
        """;
    public async Task<LearningResult> QueryAsync(LearningFilter filter, int offset = 0, CancellationToken token = default)
    {
        if (offset < 0 || filter.Grade is < 1 or > 6 || !Enum.IsDefined(filter.Kind)) throw new ArgumentOutOfRangeException(nameof(filter));
        var custom = await new CustomWordCategoryStore(new VersionedLocalStateStore(database)).ListAsync(token);
        if (filter.WordCategory is { } category && (filter.Kind != ContentKind.Word ||
            category != WordCategories.Other && !WordCategories.All.Contains(category) && !custom.Any(x => x.Id == category)))
            throw new ArgumentOutOfRangeException(nameof(filter));
        await database.InitializeAsync(token);
        using var connection = await database.OpenConnectionAsync(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$kind", filter.Kind.ToString().ToLowerInvariant());
        var basis = Joins + " WHERE " + Visible + " AND c.kind=$kind";
        command.CommandText = "SELECT count(*) " + basis;
        var unfiltered = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        var query = basis + " " + """
            AND ($difficulty IS NULL OR json_extract(c.body_json,'$.difficulty')=$difficulty)
            AND ($stage IS NULL OR json_extract(c.body_json,'$.schoolStage')=$stage)
            AND ($grade IS NULL OR json_extract(c.body_json,'$.grade')=$grade)
            AND ($personal=0 OR c.origin='personal')
            AND (json_array_length($scenes)=0 OR EXISTS(SELECT 1 FROM json_each(c.body_json,'$.scenes') s
                JOIN json_each($scenes) selected ON selected.value=s.value))
            AND ($category IS NULL OR
                ($category<>'other' AND EXISTS(SELECT 1 FROM json_each(c.body_json,'$.scenes') s WHERE s.value='word-' || $category)) OR
                ($category='other' AND NOT EXISTS(SELECT 1 FROM json_each(c.body_json,'$.scenes') s
                    JOIN json_each($categoryScenes) known ON known.value=s.value)))
            """;
        command.Parameters.AddWithValue("$difficulty", (object?)filter.Difficulty?.ToString().ToLowerInvariant() ?? DBNull.Value);
        command.Parameters.AddWithValue("$stage", (object?)filter.Stage?.ToString().ToLowerInvariant() ?? DBNull.Value);
        command.Parameters.AddWithValue("$grade", (object?)filter.Grade ?? DBNull.Value);
        command.Parameters.AddWithValue("$personal", filter.PersonalOnly ? 1 : 0);
        command.Parameters.AddWithValue("$scenes", JsonSerializer.Serialize(filter.Scenes ?? []));
        command.Parameters.AddWithValue("$category", (object?)filter.WordCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("$categoryScenes", JsonSerializer.Serialize(WordCategories.All.Concat(custom.Select(x => x.Id)).Select(WordCategories.Scene)));
        command.CommandText = "SELECT count(*) " + query;
        var total = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        var order = "c.title,c.id";
        if (filter.Kind == ContentKind.Word)
        {
            // Sort before LIMIT/OFFSET so categories and subsequent pages use one stable order.
            command.Parameters.AddWithValue("$wordOrder", JsonSerializer.Serialize(WordCategories.PreferredOrder(filter.WordCategory)));
            order = """
                COALESCE((SELECT CAST(preferred.key AS INTEGER) FROM json_each($wordOrder) preferred
                    WHERE preferred.value=COALESCE((SELECT json_extract(unit.value,'$.text')
                        FROM json_each(c.body_json,'$.textUnits') unit
                        WHERE json_extract(unit.value,'$.role')='headword' LIMIT 1),c.title) LIMIT 1),2147483647),c.title,c.id
                """;
        }
        command.CommandText = "SELECT c.id,c.title,c.body_json " + query + " ORDER BY " + order + " LIMIT 50 OFFSET $offset";
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = await command.ExecuteReaderAsync(token); var items = new List<LearningRow>();
        while (await reader.ReadAsync(token)) items.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        return new(items, total, unfiltered);
    }

    public async Task<WordCategoryCounts> GetWordCategoryCountsAsync(CancellationToken token = default)
    {
        var custom = await new CustomWordCategoryStore(new VersionedLocalStateStore(database)).ListAsync(token);
        await database.InitializeAsync(token);
        using var connection = await database.OpenConnectionAsync(token);
        using var command = connection.CreateCommand();
        // One snapshot, the same visibility rules as the lists, and no double counting repeated tags.
        command.CommandText = """
            WITH visible AS (SELECT c.id,c.body_json
            """ + " " + Joins + " WHERE " + Visible + " AND c.kind='word'), " + """
            memberships AS (
                SELECT DISTINCT v.id,known.value AS category FROM visible v,json_each(v.body_json,'$.scenes') s
                JOIN json_each($categories) known ON s.value='word-' || known.value
            )
            SELECT 'all',count(*) FROM visible
            UNION ALL SELECT category,count(*) FROM memberships GROUP BY category
            UNION ALL SELECT 'other',count(*) FROM visible v WHERE NOT EXISTS(SELECT 1 FROM memberships m WHERE m.id=v.id)
            """;
        command.Parameters.AddWithValue("$categories", JsonSerializer.Serialize(WordCategories.All.Concat(custom.Select(x => x.Id))));
        using var reader = await command.ExecuteReaderAsync(token);
        var counts = new Dictionary<string, int>();
        while (await reader.ReadAsync(token)) counts.Add(reader.GetString(0), reader.GetInt32(1));
        return new(counts["all"], counts);
    }

    /// <summary>One bounded lookup for category sharing; category overlaps produce one content ID.</summary>
    public async Task<IReadOnlyList<Guid>> GetWordIdsForCategoriesAsync(
        IReadOnlyCollection<string> categories, CancellationToken token = default)
    {
        if (categories.Count == 0) return [];
        var custom = await new CustomWordCategoryStore(new VersionedLocalStateStore(database)).ListAsync(token);
        var known = WordCategories.All.Concat(custom.Select(x => x.Id)).ToArray();
        if (categories.Any(x => x != "all" && x != WordCategories.Other && !known.Contains(x)))
            throw new ArgumentOutOfRangeException(nameof(categories));
        await database.InitializeAsync(token);
        using var connection = await database.OpenConnectionAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT c.id " + Joins + " WHERE " + Visible + " AND c.kind='word' AND " + """
            ($all=1 OR EXISTS(SELECT 1 FROM json_each(c.body_json,'$.scenes') scene
                JOIN json_each($selectedScenes) chosen ON scene.value=chosen.value) OR
            ($other=1 AND NOT EXISTS(SELECT 1 FROM json_each(c.body_json,'$.scenes') scene
                JOIN json_each($knownScenes) known ON scene.value=known.value)))
            LIMIT 101
            """;
        command.Parameters.AddWithValue("$all", categories.Contains("all") ? 1 : 0);
        command.Parameters.AddWithValue("$other", categories.Contains(WordCategories.Other) ? 1 : 0);
        command.Parameters.AddWithValue("$selectedScenes", JsonSerializer.Serialize(categories
            .Where(x => x != "all" && x != WordCategories.Other).Select(WordCategories.Scene)));
        command.Parameters.AddWithValue("$knownScenes", JsonSerializer.Serialize(known.Select(WordCategories.Scene)));
        using var reader = await command.ExecuteReaderAsync(token);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(token)) ids.Add(Guid.Parse(reader.GetString(0)));
        return ids;
    }

    public async Task<IReadOnlyList<string>> GetScenesAsync(ContentKind kind, CancellationToken token = default)
    {
        await database.InitializeAsync(token); using var connection = await database.OpenConnectionAsync(token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT s.value " + Joins + ", json_each(c.body_json,'$.scenes') s WHERE " + Visible + " AND c.kind=$kind ORDER BY s.value";
        command.Parameters.AddWithValue("$kind", kind.ToString().ToLowerInvariant());
        using var reader = await command.ExecuteReaderAsync(token); var result = new List<string>();
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(0));
        return result;
    }
}
