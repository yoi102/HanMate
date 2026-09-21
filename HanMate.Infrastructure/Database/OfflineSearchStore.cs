using System.Globalization;
using System.Text.Json;
using HanMate.Core.Content;
using HanMate.Core.Search;
using Microsoft.Data.Sqlite;
using HanMate.Infrastructure.Dictionary;

namespace HanMate.Infrastructure.Database;

public sealed record SearchSource(string Id, string Name);
public sealed record SearchCursor(string Query, string? SourceId, long Epoch, int Offset);
public sealed record WordSearchResult(Guid ContentId, string Headword, string SourceId, string SourceName,
    SearchMatchTier MatchTier, string BodyJson, bool IsReadOnly = false);
public sealed record WordSearchPage(IReadOnlyList<WordSearchResult> Items, long Epoch, int Total, SearchCursor? Next);
public sealed record DictionaryLookup(ContentDocument? Document, bool IsReadOnly, long Epoch);
public sealed class SearchCursorStaleException() : Exception("SEARCH_CURSOR_STALE");

public sealed class OfflineSearchStore(HanMateDatabase database, DefaultDictionaryStore? defaultDictionary = null)
{
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private bool _indexReady;
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        if (defaultDictionary?.IsEnabled == true) await defaultDictionary.PrepareAsync(cancellationToken).ConfigureAwait(false);
    }
    // Ownership and availability are enforced in SQL for both source selection and results.
    private const string Visible = """
        FROM content c
        LEFT JOIN resource_entry e ON e.content_id=c.id
        LEFT JOIN installed_resource r ON r.resource_id=e.resource_id
        LEFT JOIN resource_entry_override o ON o.resource_id=e.resource_id AND o.entry_id=e.entry_id
        WHERE c.kind='word' AND (
            (c.origin='personal' AND e.content_id IS NULL) OR
            (c.origin IN ('resource','builtin') AND r.resource_kind IN ('learning','dictionary')
             AND r.is_present=1 AND r.enabled=1 AND COALESCE(o.removed,0)=0))
        """ + " AND " + ContentTrashStore.Active;

    public async Task<long> GetEpochAsync(CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await Epoch(connection, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT COALESCE(r.resource_id,'personal'),COALESCE(json_extract(r.descriptor_json,'$.names.\"zh-Hans\"'),r.resource_id,''),COALESCE(r.priority,0) "
            + Visible + " ORDER BY COALESCE(r.priority,0),COALESCE(r.resource_id,'personal');";
        var result = new List<SearchSource>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new(reader.GetString(0), reader.GetString(1)));
        if (defaultDictionary?.IsEnabled == true) result.Insert(0, new(DefaultDictionaryStore.SourceId, DefaultDictionaryStore.Name));
        return result;
    }

    public async Task<WordSearchPage> SearchAsync(string input, string? sourceId = null, SearchCursor? cursor = null,
        CancellationToken cancellationToken = default, int pageSize = 50)
    {
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        var query = SearchQuery.Parse(input);
        using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: true);
        var epoch = await Epoch(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (cursor is not null && (cursor.Query != input || cursor.SourceId != sourceId || cursor.Epoch != epoch || cursor.Offset < 0))
            throw new SearchCursorStaleException();
        if (!query.IsValid) return new([], epoch, 0, null);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        var field = query.IsPinyin ? "s.pinyin_joined" : "s.hanzi_key";
        command.CommandText = """
            SELECT c.id,COALESCE(r.resource_id,'personal'),COALESCE(json_extract(r.descriptor_json,'$.names."zh-Hans"'),r.resource_id,''),
                COALESCE(r.priority,0),s.hanzi_key,s.pinyin_separated,s.tone_separated,s.frequency_rank,s.alias_ordinal
            """ + "\n" + Visible.Replace("FROM content c", "FROM search_index s JOIN content c ON c.id=s.content_id", StringComparison.Ordinal)
            + $" AND s.index_version=1 AND {field} LIKE $pattern ESCAPE '\\' AND ($source IS NULL OR COALESCE(r.resource_id,'personal')=$source);";
        command.Parameters.AddWithValue("$pattern", "%" + EscapeLike(query.Key) + "%");
        command.Parameters.AddWithValue("$source", (object?)sourceId ?? DBNull.Value);
        var best = new Dictionary<Guid, Candidate>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tier = query.Match(reader.GetString(4), reader.GetString(5), reader.GetString(6));
                if (tier is null) continue;
                var id = Guid.Parse(reader.GetString(0));
                var candidate = new Candidate(id, reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4),
                    tier.Value, reader.IsDBNull(7) ? int.MaxValue : reader.GetInt32(7), reader.GetInt32(8));
                if (!best.TryGetValue(id, out var previous) || candidate.Tier < previous.Tier
                    || candidate.Tier == previous.Tier && (candidate.Rank < previous.Rank || candidate.Rank == previous.Rank
                        && (candidate.Length < previous.Length || candidate.Length == previous.Length && candidate.Alias < previous.Alias)))
                    best[id] = candidate;
            }
        }
        if (defaultDictionary?.IsEnabled == true && (sourceId is null || sourceId == DefaultDictionaryStore.SourceId))
            foreach (var match in await defaultDictionary.FindAsync(query, cancellationToken).ConfigureAwait(false))
                best[match.Id] = new(match.Id, DefaultDictionaryStore.SourceId, DefaultDictionaryStore.Name, 1, match.Title, match.Tier, match.SenseOrder, 0);
        var offset = cursor?.Offset ?? 0;
        var page = Rank(best.Values).Skip(offset).Take(Math.Clamp(pageSize, 1, 50)).ToArray();
        var results = new List<WordSearchResult>();
        foreach (var item in page)
        {
            if (item.SourceId == DefaultDictionaryStore.SourceId)
            {
                var document = await defaultDictionary!.GetAsync(item.Id, cancellationToken).ConfigureAwait(false);
                results.Add(new(item.Id, item.Headword, item.SourceId, item.SourceName, item.Tier, JsonSerializer.Serialize(document, ContentJson.Options), true));
                continue;
            }
            command.Parameters.Clear(); command.CommandText = "SELECT body_json FROM content WHERE id=$id;";
            command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
            results.Add(new(item.Id, item.Headword, item.SourceId, item.SourceName, item.Tier,
                (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!));
        }
        transaction.Commit();
        return new(results, epoch, best.Count, offset + results.Count < best.Count ? new(input, sourceId, epoch, offset + results.Count) : null);
    }

    /// <summary>Load only exact heads, in normal search order, stopping at the requested reading.
    /// In particular, do not serialize generated dictionary documents back to JSON and parse them again.</summary>
    public async Task<DictionaryLookup> FindEntryAsync(TextUnit headword, CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: true);
        var epoch = await Epoch(connection, transaction, cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT c.id,COALESCE(r.resource_id,'personal'),COALESCE(r.priority,0),s.frequency_rank,s.alias_ordinal "
            + Visible.Replace("FROM content c", "FROM search_index s JOIN content c ON c.id=s.content_id", StringComparison.Ordinal)
            + " AND s.index_version=1 AND s.hanzi_key=$head;";
        command.Parameters.AddWithValue("$head", headword.Text.Normalize());
        var candidates = new List<Candidate>();
        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), "", reader.GetInt32(2),
                    headword.Text, SearchMatchTier.HanziExact, reader.IsDBNull(3) ? int.MaxValue : reader.GetInt32(3), reader.GetInt32(4)));
            }
        if (defaultDictionary?.IsEnabled == true)
            foreach (var match in await defaultDictionary.FindExactAsync(headword.Text, cancellationToken).ConfigureAwait(false))
                candidates.Add(new(match.Id, DefaultDictionaryStore.SourceId, "", 1, match.Title, match.Tier, match.SenseOrder, 0));
        var signature = Signature(headword);
        foreach (var candidate in Rank(candidates).DistinctBy(c => c.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readOnly = candidate.SourceId == DefaultDictionaryStore.SourceId;
            ContentDocument document;
            if (readOnly) document = await defaultDictionary!.GetAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
            else
            {
                command.Parameters.Clear(); command.CommandText = "SELECT body_json FROM content WHERE id=$id;";
                command.Parameters.AddWithValue("$id", candidate.Id.ToString("D"));
                document = JsonSerializer.Deserialize<ContentDocument>((string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!, ContentJson.Options)!;
            }
            var head = document.TextUnits.Single(u => u.Role == TextUnitRole.Headword);
            if (head.Text != headword.Text || Signature(head) != signature || !document.TextUnits.Any(u => u.Role == TextUnitRole.Definition)) continue;
            transaction.Commit();
            return new(document, readOnly, epoch);
        }
        transaction.Commit();
        return new(null, false, epoch);

        static string Signature(TextUnit unit) => string.Join(" ", unit.Tokens.Select(t => t.Pinyin?.Base + t.Pinyin?.Tone));
    }

    private static IOrderedEnumerable<Candidate> Rank(IEnumerable<Candidate> candidates) => candidates
        .OrderBy(c => c.Tier).ThenBy(c => c.Priority).ThenBy(c => c.Rank).ThenBy(c => c.Length)
        .ThenBy(c => c.SourceId, StringComparer.Ordinal).ThenBy(c => c.Id.ToString("D"), StringComparer.Ordinal);

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (_indexReady) return;
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_indexReady) return;
            using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                SELECT c.body_json FROM content c WHERE c.kind='word' AND
                (NOT EXISTS(SELECT 1 FROM search_index s WHERE s.content_id=c.id) OR
                 EXISTS(SELECT 1 FROM search_index s WHERE s.content_id=c.id AND s.index_version<>1));
                """;
            var documents = new List<ContentDocument>();
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var document = JsonSerializer.Deserialize<ContentDocument>(reader.GetString(0), ContentJson.Options)!;
                    if (!new ContentDocumentValidator().Validate(document).IsValid) throw new InvalidDataException("SEARCH_INDEX_INVALID_CONTENT");
                    documents.Add(document);
                }
            foreach (var document in documents)
                await WordSearchIndex.ReplaceAsync(connection, transaction, document, cancellationToken).ConfigureAwait(false);
            if (documents.Count > 0)
            {
                command.CommandText = "UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1;";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested(); transaction.Commit(); _indexReady = true;
        }
        finally { _indexGate.Release(); }
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    private async Task<long> Epoch(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken token)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT data_epoch FROM app_state WHERE singleton=1;";
        return checked(Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) + (defaultDictionary?.Revision ?? 0));
    }
    private sealed record Candidate(Guid Id, string SourceId, string SourceName, int Priority, string Headword,
        SearchMatchTier Tier, int Rank, int Alias)
    {
        public int Length { get; } = new StringInfo(Headword).LengthInTextElements;
    }
}
