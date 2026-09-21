using System.Text.Json;
using HanMate.Core.Resources;
using Microsoft.Data.Sqlite;

namespace HanMate.Infrastructure.Database;

public sealed class SearchAliasStore(HanMateDatabase database)
{
    public async Task ReplaceAsync(
        Guid contentId,
        IReadOnlyCollection<SearchAliasValue> aliases,
        CancellationToken cancellationToken = default)
    {
        if (contentId == Guid.Empty) throw new ArgumentException("Content ID must not be empty.", nameof(contentId));
        ArgumentNullException.ThrowIfNull(aliases);
        if (aliases.Select(alias => alias.AliasOrdinal).Distinct().Count() != aliases.Count)
            throw new ArgumentException("Alias ordinals must be unique per content.", nameof(aliases));
        foreach (var alias in aliases) Validate(alias);

        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM search_index WHERE content_id=$content;";
            deleteCommand.Parameters.AddWithValue("$content", contentId.ToString("D"));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var alias in aliases.OrderBy(alias => alias.AliasOrdinal))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO search_index(
                    content_id,alias_ordinal,hanzi_key,pinyin_joined,pinyin_separated,
                    tone_joined,tone_separated,syllables_json,frequency_rank,index_version)
                VALUES($content,$ordinal,$hanzi,$joined,$separated,$toneJoined,$toneSeparated,$syllables,$rank,$version);
                """;
            command.Parameters.AddWithValue("$content", contentId.ToString("D"));
            command.Parameters.AddWithValue("$ordinal", alias.AliasOrdinal);
            command.Parameters.AddWithValue("$hanzi", alias.HanziKey);
            command.Parameters.AddWithValue("$joined", alias.PinyinJoined);
            command.Parameters.AddWithValue("$separated", alias.PinyinSeparated);
            command.Parameters.AddWithValue("$toneJoined", alias.ToneJoined);
            command.Parameters.AddWithValue("$toneSeparated", alias.ToneSeparated);
            command.Parameters.AddWithValue("$syllables", alias.SyllablesJson);
            command.Parameters.AddWithValue("$rank", alias.FrequencyRank is null ? DBNull.Value : alias.FrequencyRank.Value);
            command.Parameters.AddWithValue("$version", alias.IndexVersion);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var epochCommand = connection.CreateCommand())
        {
            epochCommand.Transaction = transaction;
            epochCommand.CommandText = "UPDATE app_state SET data_epoch=data_epoch+1 WHERE singleton=1;";
            await epochCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchAliasValue>> GetAsync(Guid contentId, CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT alias_ordinal,hanzi_key,pinyin_joined,pinyin_separated,tone_joined,
                   tone_separated,syllables_json,frequency_rank,index_version
            FROM search_index WHERE content_id=$content ORDER BY alias_ordinal;
            """;
        command.Parameters.AddWithValue("$content", contentId.ToString("D"));
        var aliases = new List<SearchAliasValue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            aliases.Add(new SearchAliasValue(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.GetInt32(8)));
        }

        return aliases;
    }

    private static void Validate(SearchAliasValue alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (alias.AliasOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(alias));
        if (alias.IndexVersion < 1) throw new ArgumentOutOfRangeException(nameof(alias));
        ArgumentNullException.ThrowIfNull(alias.HanziKey);
        ArgumentNullException.ThrowIfNull(alias.PinyinJoined);
        ArgumentNullException.ThrowIfNull(alias.PinyinSeparated);
        ArgumentNullException.ThrowIfNull(alias.ToneJoined);
        ArgumentNullException.ThrowIfNull(alias.ToneSeparated);
        using var _ = JsonDocument.Parse(alias.SyllablesJson);
    }
}
